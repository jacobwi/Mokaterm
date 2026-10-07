using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Connections;

/// <summary>Creates or edits a host: address, name, folder, environment, color, tags and notes.</summary>
public sealed partial class HostEditor : ShellEditorBase<HostEditorRequest>
{
	// The Moka status palette, so host colors sit well with the rest of the UI. Named, because a bare swatch says
	// nothing: the field above the row repeats the name of whichever one is in force.
	private static readonly (string Name, string Color)[] ColorPresets =
	[
		("Red", "#ef5350"),
		("Orange", "#ffab40"),
		("Amber", "#ffd54f"),
		("Green", "#00e676"),
		("Blue", "#42a5f5"),
		("Purple", "#ce93d8"),
	];

	private HostProfile? _original;
	private Dictionary<Guid, string> _folderPaths = [];
	private IReadOnlyList<Guid?> _folderOptions = [];
	private IReadOnlyList<HostProfile> _otherHosts = [];
	private IReadOnlyList<string> _tagSuggestions = [];
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
	private string? _clash;

	[Inject]
	private CatalogState Catalog { get; set; } = default!;

	[Inject]
	private IConnectionRepository Repository { get; set; } = default!;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private ITerminalThemeCatalog Themes { get; set; } = default!;

	[Inject]
	private ILogger<HostEditor> Logger { get; set; } = default!;

	protected override bool IsBusy => _saving;

	/// <summary>The address as it would be stored, which is what the preview and the clash check read.</summary>
	private string PreviewAddress => HostAddress.Normalize(_address);

	private string PreviewName =>
		_name.Trim() is { Length: > 0 } name ? name
		: PreviewAddress.Length > 0 ? PreviewAddress
		: "New host";

	/// <summary>The tree only repeats the address beside a name that differs from it, so the preview does the same.</summary>
	private bool ShowPreviewAddress =>
		PreviewAddress.Length > 0 && !string.Equals(PreviewName, PreviewAddress, StringComparison.Ordinal);

	/// <summary>The color this host is drawn in everywhere, the one taken from its address included.</summary>
	private string Accent => HostAccent.For(PreviewAddress, _color);

	private string AutomaticAccent => HostAccent.For(PreviewAddress);

	/// <summary>The preset the field holds, so a choice can be named instead of only shown as six hex digits.</summary>
	private string? PresetName =>
		HexColor.Normalize(_color) is { } color
			? ColorPresets
				.Where(preset => string.Equals(preset.Color, color, StringComparison.OrdinalIgnoreCase))
				.Select(preset => preset.Name)
				.FirstOrDefault()
			: null;

	private string ColorHelp =>
		HexColor.Normalize(_color) is null
			? "Without one of its own, a host is drawn in a color taken from its address."
			: PresetName is { } name
				? $"{name}. Marks the host in the tree, on its tabs and in the status bar."
				: "Marks the host in the tree, on its tabs and in the status bar.";

	/// <summary>
	/// What the environment does: the tag every view draws, and the terminal theme it resolves to. The theme is the part
	/// nobody reads off a row of names, so this names the theme in force or says where one is set.
	/// </summary>
	private string EnvironmentHelp
	{
		get
		{
			if (_environment == HostEnvironment.None)
			{
				return "Not tagged. Sessions use the terminal theme from the settings.";
			}

			return Settings.Get<TerminalSettings>().ThemeIdFor(_environment) is { } themeId
				? $"Tags the host in the tree, on its tabs and in the status bar, and opens its sessions in the {Themes.Get(themeId).Name} theme."
				: "Tags the host in the tree, on its tabs and in the status bar. Settings, Terminal can also give it a theme of its own.";
		}
	}

	protected override async Task LoadAsync(HostEditorRequest request)
	{
		await Catalog.EnsureLoadedAsync();
		if (IsCurrent(request))
		{
			Load(request, Catalog.Catalog);
		}
	}

	/// <summary>
	/// Drops the catalog with the request. The load that fills a new one awaits the catalog first, so keeping these would
	/// show the host just closed inside the dialog for the host just opened, and addresses, names and tags are vault data
	/// that has no reason to sit here while no editor is open.
	/// </summary>
	protected override void Reset()
	{
		_original = null;
		_folderPaths = [];
		_folderOptions = [];
		_otherHosts = [];
		_tagSuggestions = [];
		_address = "";
		_name = "";
		_folderId = null;
		_environment = HostEnvironment.None;
		_color = "";
		_tags = new List<string>();
		_notes = "";
		_clash = null;
		_error = null;
		_addressError = null;
		_colorError = null;
	}

	private static string AriaPressed(bool pressed) => pressed ? "true" : "false";

	private string FolderLabel(Guid? folderId) =>
		folderId is { } id && _folderPaths.TryGetValue(id, out string? path) ? path : "Top level";

	private bool IsChosen(string color) =>
		string.Equals(HexColor.Normalize(_color), color, StringComparison.OrdinalIgnoreCase);

	private string SwatchClass(string color) => IsChosen(color) ? "mt-swatch mt-swatch--chosen" : "mt-swatch";

	private void Load(HostEditorRequest request, ConnectionCatalog catalog)
	{
		_saving = false;
		_error = null;
		_addressError = null;
		_colorError = null;
		_clash = null;
		_folderPaths = FolderPaths.Build(catalog);
		_folderOptions = [null, .. FolderPaths.Sorted(_folderPaths).Select(pair => (Guid?)pair.Key)];

		_original = request.HostId is { } hostId ? catalog.FindHost(hostId) : null;
		if (request.HostId is not null && _original is null)
		{
			_error = "This host no longer exists.";
		}

		// Every other saved host: what an address and a name are compared against. Tags come from all of them, so the
		// same tag is picked from a list rather than retyped as "prod", "Prod" and "production".
		_otherHosts = [.. catalog.Hosts.Where(host => host.Id != _original?.Id)];
		_tagSuggestions =
		[
			.. catalog.Hosts
				.SelectMany(host => host.Tags)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase),
		];

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
		UpdateClash();
	}

	private void OnEnvironmentChanged(string value) =>
		_environment = Enum.TryParse(value, out HostEnvironment environment) ? environment : HostEnvironment.None;

	/// <summary>Validates while the address is typed, so an empty or malformed one is reported where it is entered.</summary>
	private void OnAddressChanged()
	{
		_addressError = HostAddress.Validate(_address);
		UpdateClash();
	}

	private void OnColorChanged() =>
		_colorError = _color.Trim().Length > 0 && HexColor.Normalize(_color) is null ? HexColor.Requirement() : null;

	private void PickColor(string color)
	{
		_color = color;
		_colorError = null;
	}

	private void PickAutomatic()
	{
		_color = "";
		_colorError = null;
	}

	/// <summary>
	/// Says so when another host already answers to this address or reads the same in the tree. Neither refuses the save:
	/// one machine behind two names is legitimate, and two rows that look alike are the user's call.
	/// </summary>
	private void UpdateClash()
	{
		_clash = null;
		string address = PreviewAddress;
		if (address.Length == 0)
		{
			return;
		}

		if (_otherHosts.FirstOrDefault(host => string.Equals(host.Address, address, StringComparison.OrdinalIgnoreCase)) is { } sameAddress)
		{
			_clash = $"{sameAddress.DisplayName} already uses this address. One host carries any number of logins, so it rarely needs a second entry.";
			return;
		}

		string display = PreviewName;
		if (_otherHosts.Any(host => string.Equals(host.DisplayName, display, StringComparison.OrdinalIgnoreCase)))
		{
			_clash = $"Another host is already shown as {display}. Both rows will read the same in the tree.";
		}
	}

	private async Task SaveAsync(bool addLogin)
	{
		if (Request is null || _saving)
		{
			return;
		}

		_addressError = HostAddress.Validate(_address);
		string? color = HexColor.Normalize(_color);
		_colorError = !string.IsNullOrWhiteSpace(_color) && color is null ? HexColor.Requirement() : null;
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
