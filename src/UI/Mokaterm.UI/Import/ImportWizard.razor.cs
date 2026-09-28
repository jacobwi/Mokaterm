using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Import;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Import;

/// <summary>
/// Brings saved hosts over from another client: pick a source, tick what to keep, choose where it lands. Nothing is
/// written until the last step, and no secret ever crosses.
/// </summary>
public sealed partial class ImportWizard : ShellEditorBase<ImportEditorRequest>
{
	private const string MutedStyle = "color:var(--moka-color-on-surface-variant)";

	/// <summary>A picked config or registry export past this is not one, so it is refused rather than read in part.</summary>
	private const int MaxFileBytes = 16 * 1024 * 1024;

	private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
	private List<ImportSourceRow> _sources = [];
	private List<Guid?> _folderOptions = [null];
	private Dictionary<Guid, string> _folderPaths = [];
	private ImportPreview? _preview;
	private ImportResult? _result;
	private ImportStep _step;
	private Guid? _folderId;
	private string _newFolderName = "";
	private string? _error;
	private bool _probing;
	private bool _busy;

	[Inject]
	private IConnectionImporter Importer { get; set; } = default!;

	[Inject]
	private CatalogState Catalog { get; set; } = default!;

	[Inject]
	private IProtocolRegistry Protocols { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private IServiceProvider Services { get; set; } = default!;

	[Inject]
	private ILogger<ImportWizard> Logger { get; set; } = default!;

	private bool CanPickFiles => Services.GetService<ILocalFileAccess>() is not null;

	private string SourceName =>
		_preview is { } preview && _sources.FirstOrDefault(row => row.Info.Id == preview.SourceId) is { } row
			? row.Info.DisplayName
			: "The picked file";

	private string ImportLabel => _selected.Count switch
	{
		0 => "Import",
		1 => "Import 1 host",
		_ => string.Create(CultureInfo.CurrentCulture, $"Import {_selected.Count} hosts"),
	};

	protected override bool IsBusy => _busy;

	protected override bool SubmitOnEnter => true;

	protected override void Reset()
	{
		_step = ImportStep.Source;
		_preview = null;
		_result = null;
		_error = null;
		_selected.Clear();
		_newFolderName = "";
		_sources = [];
	}

	protected override async Task LoadAsync(ImportEditorRequest request)
	{
		_folderId = request.FolderId;
		await ProbeAsync(request);
	}

	private static string CountText(int count) => count == 1
		? "1 host"
		: string.Create(CultureInfo.CurrentCulture, $"{count} hosts");

	private static string PickLabel(ImportSourceInfo info) =>
		info.FileExtensions.Count > 0 ? $"Pick a {string.Join(" or ", info.FileExtensions)} file" : "Pick a file";

	private static string EntryTitle(ImportedEntry entry) => entry.Name;

	private static string ResultHeadline(ImportResult result) => result.LoginsCreated switch
	{
		0 => "Nothing was imported.",
		1 => "One login was imported.",
		_ => string.Create(CultureInfo.CurrentCulture, $"{result.LoginsCreated} logins were imported."),
	};

	private static string ResultDetail(ImportResult result)
	{
		List<string> parts = [];
		if (result.HostsCreated > 0)
		{
			parts.Add(result.HostsCreated == 1 ? "1 new host" : string.Create(CultureInfo.CurrentCulture, $"{result.HostsCreated} new hosts"));
		}

		if (result.HostsReused > 0)
		{
			parts.Add(result.HostsReused == 1 ? "1 login added to a host you already had" : string.Create(CultureInfo.CurrentCulture, $"{result.HostsReused} logins added to hosts you already had"));
		}

		if (result.FoldersCreated > 0)
		{
			parts.Add(result.FoldersCreated == 1 ? "1 new folder" : string.Create(CultureInfo.CurrentCulture, $"{result.FoldersCreated} new folders"));
		}

		return parts.Count == 0
			? "Nothing new reached the connections panel."
			: string.Join(", ", parts) + ". Passwords and keys were not imported, so each login asks the first time you connect.";
	}

	private static string SourceDetail(ImportSourceRow row)
	{
		if (row.Preview.Entries.Count > 0)
		{
			return row.Preview.Location ?? row.Info.Description ?? "";
		}

		return row.Preview.Message ?? row.Info.Description ?? "";
	}

	private static string SourceClass(ImportSourceRow row) =>
		row.Preview.Entries.Count > 0 ? "mt-import-source mt-import-source--found" : "mt-import-source";

	private string Endpoint(ImportedEntry entry)
	{
		int port = entry.Port ?? Protocols.FindDescriptor(entry.ProtocolId)?.DefaultPort ?? 0;
		string endpoint = port > 0 ? EndpointFormat.Format(entry.Username, entry.Address, port) : entry.Address;
		return $"{endpoint} ({Protocols.DisplayName(entry.ProtocolId)})";
	}

	private string FolderLabel(Guid? folderId) =>
		folderId is { } id && _folderPaths.TryGetValue(id, out string? path) ? path : "Top level";

	private async Task ProbeAsync(ImportEditorRequest request)
	{
		_probing = true;

		// Reading the registry and a config file takes long enough to be worth a spinner.
		StateHasChanged();
		try
		{
			List<ImportSourceRow> rows = [];
			foreach (ImportSourceInfo info in Importer.Sources)
			{
				ImportPreview preview = await Importer.PreviewAsync(info.Id, new ImportReadRequest());
				if (!IsCurrent(request))
				{
					return;
				}

				rows.Add(new ImportSourceRow(info, preview));
			}

			_sources = rows;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Looking for importable hosts failed.");
			_error = "The saved hosts on this machine could not be read.";
		}
		finally
		{
			_probing = false;
		}
	}

	private async Task ChooseAsync(ImportSourceRow row)
	{
		await Catalog.EnsureLoadedAsync();
		ShowPreview(row.Preview);
	}

	private void ShowPreview(ImportPreview preview)
	{
		_preview = preview;
		_error = null;
		_selected.Clear();

		// Entries that already match a saved login start unticked, so a second import adds nothing by accident.
		foreach (ImportedEntry entry in preview.Entries.Where(entry => !entry.AlreadyExists))
		{
			_selected.Add(entry.Key);
		}

		ConnectionCatalog catalog = Catalog.Catalog;
		_folderPaths = FolderPaths.Build(catalog);
		_folderOptions = [null, .. FolderPaths.Sorted(_folderPaths).Select(pair => (Guid?)pair.Key)];
		if (_folderId is { } chosen && !_folderPaths.ContainsKey(chosen))
		{
			_folderId = null;
		}

		_step = ImportStep.Preview;
	}

	private async Task PickAsync(ImportSourceRow row)
	{
		if (_busy || Services.GetService<ILocalFileAccess>() is not { } files)
		{
			return;
		}

		_busy = true;
		try
		{
			PickedFile<byte[]> picked = await LocalFilePick.ReadBytesAsync(files, MaxFileBytes, "a saved session list");
			if (!picked.WasRead)
			{
				// Null after the picker was closed, which clears whatever the last attempt said.
				_error = picked.Error;
				return;
			}

			ImportPreview preview = await Importer.PreviewAsync(
				row.Info.Id,
				new ImportReadRequest(new ImportFile { Name = picked.Name, Content = picked.Content }));

			if (preview.Entries.Count == 0)
			{
				_error = preview.Message ?? $"{picked.Name} has nothing to import.";
				return;
			}

			await Catalog.EnsureLoadedAsync();
			ShowPreview(preview);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Reading a picked import file failed.");
			_error = "The file could not be read.";
		}
		finally
		{
			_busy = false;
		}
	}

	private void OnRowKeyDown(KeyboardEventArgs args, string key)
	{
		if (args.Key is " " or "Enter")
		{
			Toggle(key);
		}
	}

	private void Toggle(string key)
	{
		if (_busy)
		{
			return;
		}

		if (!_selected.Remove(key))
		{
			_selected.Add(key);
		}
	}

	private void SelectAll()
	{
		if (_preview is { } preview)
		{
			_selected.Clear();
			_selected.UnionWith(preview.Entries.Select(entry => entry.Key));
		}
	}

	private void SelectNone() => _selected.Clear();

	private void BackToSources()
	{
		_step = ImportStep.Source;
		_preview = null;
		_error = null;
		_selected.Clear();
	}

	private async Task ImportAsync()
	{
		if (_busy || _preview is not { } preview || _selected.Count == 0)
		{
			return;
		}

		_busy = true;
		_error = null;
		try
		{
			_result = await Importer.ImportAsync(new ImportRequest
			{
				Entries = [.. preview.Entries.Where(entry => _selected.Contains(entry.Key))],
				FolderId = _folderId,
				NewFolderName = _newFolderName,
			});

			_step = ImportStep.Done;
			await Catalog.RefreshAsync();
			if (_result.Error is null && _result.LoginsCreated > 0)
			{
				Interaction.Notify(NoticeSeverity.Success, ResultHeadline(_result));
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Importing saved hosts failed.");
			_error = "The import could not be finished.";
		}
		finally
		{
			_busy = false;
		}
	}

}
