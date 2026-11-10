using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.Primitives.DataList;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.FileBrowser.Browsing;

namespace Mokaterm.UI.FileBrowser.Details;

/// <summary>
/// The file browser's details pane: what the listing says about the selection, and for one file the start of its text
/// or the image itself. The file is read only while the pane is shown and the browser visible, once the selection has
/// stayed on it for <see cref="SettleDelay"/>, and the read is cancelled as soon as the selection moves on.
/// </summary>
public sealed partial class EntryDetails : ComponentBase, IDisposable
{
	/// <summary>Arrowing through a folder passes many files; only the one the selection rests on is read.</summary>
	internal static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(300);

	private CancellationTokenSource? _reading;
	private PreviewTarget? _target;
	private EntryPreview? _preview;
	private ReadPhase _phase;
	private bool _imageBroken;

	/// <summary>The selected entries, none when nothing is selected.</summary>
	[Parameter]
	[EditorRequired]
	public IReadOnlyList<RemoteFileEntry> Entries { get; set; } = [];

	/// <summary>Where the preview is read from, root's file system in root mode, and what decides the facts shown.</summary>
	[Parameter]
	public IRemoteFileSystem? FileSystem { get; set; }

	/// <summary>False while the browser is hidden, which stops a read and starts none.</summary>
	[Parameter]
	public bool Active { get; set; } = true;

	/// <summary>The folder on screen, described when nothing is selected.</summary>
	[Parameter]
	public string Folder { get; set; } = RemotePath.Root;

	/// <summary>How many items the folder shows, in the browser's own words.</summary>
	[Parameter]
	public string? FolderSummary { get; set; }

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	private RemoteFileEntry? Single => Entries.Count == 1 ? Entries[0] : null;

	private string FolderName => RemotePath.IsRoot(Folder) ? "/" : RemotePath.GetName(Folder);

	private static string CutNote => string.Create(CultureInfo.CurrentCulture, $"The first {DisplayFormat.Bytes(EntryPreviews.TextBytes)} of the file.");

	private static string TooLargeNote => string.Create(CultureInfo.CurrentCulture, $"Too large to preview: images are shown up to {DisplayFormat.Bytes(EntryPreviews.ImageBytes)}.");

	private List<MokaDataListItem> FolderFacts
	{
		get
		{
			List<MokaDataListItem> facts = [];
			if (FolderSummary is { Length: > 0 } summary)
			{
				facts.Add(new MokaDataListItem("Items", summary));
			}

			facts.Add(new MokaDataListItem("Path", null, PathValue(Folder)));
			return facts;
		}
	}

	private List<MokaDataListItem> SelectionFacts
	{
		get
		{
			List<MokaDataListItem> facts = [new MokaDataListItem("Selected", EntryVisuals.KindSummary(Entries))];
			List<RemoteFileEntry> files = [.. Entries.Where(entry => entry.Kind == RemoteEntryKind.File)];
			if (files.Count > 0)
			{
				facts.Add(new MokaDataListItem("Size of the files", EntryVisuals.DetailedSize(files.Sum(file => file.Size))));
			}

			return facts;
		}
	}

	public void Dispose() => StopReading();

	protected override async Task OnParametersSetAsync()
	{
		PreviewTarget? wanted = Single is { } entry && FileSystem is { } fileSystem && EntryPreviews.KindOf(entry) != EntryPreviewKind.None
			? new PreviewTarget(fileSystem, entry)
			: null;

		if (wanted == _target && (_preview is not null || _phase != ReadPhase.Idle))
		{
			// Hidden halfway through a read: it stops, and starts again once the browser shows.
			if (!Active && _phase != ReadPhase.Idle)
			{
				Forget();
			}

			return;
		}

		Forget();
		if (wanted is null || !Active)
		{
			return;
		}

		await ReadAsync(wanted);
	}

	private async Task ReadAsync(PreviewTarget target)
	{
		CancellationTokenSource reading = new();
		CancellationToken token = reading.Token;
		_reading = reading;
		_target = target;
		_phase = ReadPhase.Settling;
		try
		{
			await Task.Delay(SettleDelay, Time, token);

			// The delay can end on the renderer's queue behind the parameters that replaced it.
			if (!ReferenceEquals(_reading, reading))
			{
				return;
			}

			_phase = ReadPhase.Reading;
			StateHasChanged();
			EntryPreview preview = await EntryPreviews.LoadAsync(target.FileSystem, target.Entry, token);
			if (ReferenceEquals(_reading, reading))
			{
				_reading = null;
				reading.Dispose();
				_preview = preview;
				_phase = ReadPhase.Idle;
			}
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			// The selection moved on, the browser hid or the pane closed.
		}
	}

	private void Forget()
	{
		StopReading();
		_target = null;
		_preview = null;
		_phase = ReadPhase.Idle;
		_imageBroken = false;
	}

	private void StopReading()
	{
		if (Interlocked.Exchange(ref _reading, null) is { } reading)
		{
			reading.Cancel();
			reading.Dispose();
		}
	}

	private void OnImageFailed() => _imageBroken = true;

	private List<MokaDataListItem> FactsOf(RemoteFileEntry entry)
	{
		List<MokaDataListItem> facts = [new MokaDataListItem("Kind", EntryVisuals.KindLabel(entry))];
		if (entry.Kind == RemoteEntryKind.File)
		{
			facts.Add(new MokaDataListItem("Size", EntryVisuals.DetailedSize(entry.Size)));
		}

		if (entry.LastModified is { } modified)
		{
			facts.Add(new MokaDataListItem("Modified", DisplayFormat.Timestamp(modified)));
		}

		if (Supports(RemoteFileSystemFeatures.Permissions) && entry.Permissions is { } mode)
		{
			facts.Add(new MokaDataListItem("Permissions", EntryVisuals.PermissionsText(entry) + " (" + UnixFileModeFormat.ToOctal(mode) + ")"));
		}

		if (Supports(RemoteFileSystemFeatures.Ownership) && EntryOrdering.OwnerLabel(entry) is { Length: > 0 } owner)
		{
			facts.Add(new MokaDataListItem("Owner", owner));
		}

		if (entry.Kind == RemoteEntryKind.SymbolicLink)
		{
			facts.Add(new MokaDataListItem("Link target", (entry.LinkTarget ?? "unknown") + " (" + EntryVisuals.LinkTargetKindLabel(entry) + ")"));
		}

		facts.Add(new MokaDataListItem("Path", null, PathValue(entry.Path)));
		return facts;
	}

	private bool Supports(RemoteFileSystemFeatures feature) => FileSystem is { } fileSystem && (fileSystem.Features & feature) == feature;

	private enum ReadPhase
	{
		Idle,
		Settling,
		Reading,
	}

	private sealed record PreviewTarget(IRemoteFileSystem FileSystem, RemoteFileEntry Entry);
}
