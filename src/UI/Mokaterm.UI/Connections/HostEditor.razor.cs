using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Connections;

/// <summary>Creates or edits a host: address, name, folder, environment, color, tags and notes.</summary>
public sealed partial class HostEditor : ShellEditorBase<HostEditorRequest>
{
	// The Moka status palette, so host colors sit well with the rest of the UI.
	private static readonly string[] ColorPresets = ["#ef5350", "#ffab40", "#ffd54f", "#00e676", "#42a5f5", "#ce93d8"];

	private HostProfile? _original;
	private Dictionary<Guid, string> _folderPaths = [];
	private IReadOnlyList<Guid?> _folderOptions = [];
	private string _address = "";
	private string _name = "";
	private Guid? _folderId;
	private HostEnvironment _environment;
	private string _color = "";
	private IList<string> _tags = new List<string>();
	private string _notes = "";
	private bool _saving;
	private string? _error;
	private string? _addressError;
	private string? _colorError;

	[Inject]
	private CatalogState Catalog { get; set; } = default!;

	[Inject]
	private IConnectionRepository Repository { get; set; } = default!;

	[Inject]
	private ILogger<HostEditor> Logger { get; set; } = default!;

	protected override bool IsBusy => _saving;

	protected override async Task LoadAsync(HostEditorRequest request)
	{
		await Catalog.EnsureLoadedAsync();
		if (IsCurrent(request))
		{
			Load(request, Catalog.Catalog);
		}
	}

	protected override void Reset() => _original = null;

	private string FolderLabel(Guid? folderId) =>
		folderId is { } id && _folderPaths.TryGetValue(id, out string? path) ? path : "Top level";

	private void Load(HostEditorRequest request, ConnectionCatalog catalog)
	{
		_saving = false;
		_error = null;
		_addressError = null;
		_colorError = null;
		_folderPaths = FolderPaths.Build(catalog);
		_folderOptions = [null, .. FolderPaths.Sorted(_folderPaths).Select(pair => (Guid?)pair.Key)];

		_original = request.HostId is { } hostId ? catalog.FindHost(hostId) : null;
		if (request.HostId is not null && _original is null)
		{
			_error = "This host no longer exists.";
		}

		_address = _original?.Address ?? "";
		_name = _original?.Name ?? "";
		_folderId = _original?.FolderId ?? request.FolderId;
		if (_folderId is { } folderId && !_folderPaths.ContainsKey(folderId))
		{
			_folderId = null;
		}

		_environment = _original?.Environment ?? HostEnvironment.None;
		_color = _original?.Color ?? "";
		_tags = _original is null ? new List<string>() : new List<string>(_original.Tags);
		_notes = _original?.Notes ?? "";
	}

	private void OnEnvironmentChanged(string value) =>
		_environment = Enum.TryParse(value, out HostEnvironment environment) ? environment : HostEnvironment.None;

	private async Task SaveAsync(bool addLogin)
	{
		if (Request is null || _saving)
		{
			return;
		}

		_addressError = HostAddress.Validate(_address);
		string? color = HexColor.Normalize(_color);
		_colorError = !string.IsNullOrWhiteSpace(_color) && color is null ? "Use a hex color such as #ef5350." : null;
		if (_addressError is not null || _colorError is not null || (Request.HostId is not null && _original is null))
		{
			return;
		}

		_saving = true;
		_error = null;
		try
		{
			HostProfile host = (_original ?? new HostProfile { Id = Guid.NewGuid(), Address = "" }) with
			{
				Address = HostAddress.Normalize(_address),
				Name = _name.Trim(),
				FolderId = _folderId,
				Environment = _environment,
				Color = color,
				Tags = [.. _tags.Select(tag => tag.Trim()).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)],
				Notes = string.IsNullOrWhiteSpace(_notes) ? null : _notes.Trim(),
			};

			await Repository.SaveHostAsync(host);
			_saving = false;
			CloseSaved();
			if (addLogin)
			{
				OpenEditor(new ConnectionEditorRequest { HostId = host.Id });
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Saving a host failed.");
			_error = VaultFailure.Describe(ex, "save this host");
			_saving = false;
		}
	}
}
