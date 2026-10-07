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
	private IReadOnlyList<string> _siblingNames = [];
	private string _name = "";
	private bool _saving;
	private string? _error;
	private string? _clash;

	[Inject]
	private CatalogState Catalog { get; set; } = default!;

	[Inject]
	private IConnectionRepository Repository { get; set; } = default!;

	[Inject]
	private ILogger<FolderEditor> Logger { get; set; } = default!;

	private string ParentText => _parentPath is null ? "Created at the top level." : $"Inside {_parentPath}.";

	protected override bool IsBusy => _saving;

	protected override bool SubmitOnEnter => true;

	/// <summary>
	/// Drops the catalog with the request: the load awaits it, so keeping these would show the folder just closed inside
	/// the dialog for the folder just opened.
	/// </summary>
	protected override void Reset()
	{
		_original = null;
		_parentPath = null;
		_siblingNames = [];
		_name = "";
		_clash = null;
		_error = null;
	}

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
		_clash = null;
		_original = request.FolderId is { } folderId ? catalog.FindFolder(folderId) : null;
		if (request.FolderId is not null && _original is null)
		{
			_error = "This folder no longer exists.";
		}

		_name = _original?.Name ?? "";
		Guid? parentId = _original?.ParentId ?? request.ParentId;
		_parentPath = parentId is { } parent && paths.TryGetValue(parent, out string? path) ? path : null;
		_siblingNames = [.. catalog.FoldersIn(parentId).Where(folder => folder.Id != _original?.Id).Select(folder => folder.Name)];
		UpdateClash();
	}

	/// <summary>
	/// Says so when a folder beside this one is already called the same. Not a refusal: two folders may share a name under
	/// different parents, and here it only means two rows that read alike.
	/// </summary>
	private void UpdateClash()
	{
		string name = _name.Trim();
		_clash = name.Length > 0 && _siblingNames.Contains(name, StringComparer.OrdinalIgnoreCase)
			? $"A folder here is already called {name}."
			: null;
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
