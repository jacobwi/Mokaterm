using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Connections;

/// <summary>Creates a folder or renames one.</summary>
public sealed partial class FolderEditor : ShellEditorBase<FolderEditorRequest>
{
	private ConnectionFolder? _original;
	private string? _parentPath;
	private string _name = "";
	private bool _saving;
	private string? _error;

	[Inject]
	private CatalogState Catalog { get; set; } = default!;

	[Inject]
	private IConnectionRepository Repository { get; set; } = default!;

	[Inject]
	private ILogger<FolderEditor> Logger { get; set; } = default!;

	private string ParentText => _parentPath is null ? "Created at the top level." : $"Inside {_parentPath}.";

	protected override bool IsBusy => _saving;

	protected override bool SubmitOnEnter => true;

	protected override void Reset() => _original = null;

	protected override async Task LoadAsync(FolderEditorRequest request)
	{
		await Catalog.EnsureLoadedAsync();
		if (!IsCurrent(request))
		{
			return;
		}

		ConnectionCatalog catalog = Catalog.Catalog;
		Dictionary<Guid, string> paths = FolderPaths.Build(catalog);
		_saving = false;
		_error = null;
		_original = request.FolderId is { } folderId ? catalog.FindFolder(folderId) : null;
		if (request.FolderId is not null && _original is null)
		{
			_error = "This folder no longer exists.";
		}

		_name = _original?.Name ?? "";
		Guid? parentId = _original?.ParentId ?? request.ParentId;
		_parentPath = parentId is { } parent && paths.TryGetValue(parent, out string? path) ? path : null;
	}

	private async Task SaveAsync()
	{
		string name = _name.Trim();
		if (Request is not { } request || _saving || name.Length == 0 || (request.FolderId is not null && _original is null))
		{
			return;
		}

		_saving = true;
		_error = null;
		try
		{
			ConnectionFolder folder = _original is null
				? new ConnectionFolder { Id = Guid.NewGuid(), Name = name, ParentId = request.ParentId }
				: _original with { Name = name };
			await Repository.SaveFolderAsync(folder);
			_saving = false;
			CloseSaved();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Saving a folder failed.");
			_error = VaultFailure.Describe(ex, "save this folder");
			_saving = false;
		}
	}
}
