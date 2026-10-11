using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.Core.Enums;
using Moka.Red.Feedback.Dialog;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Platform;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Compare;
using Mokaterm.UI.FileBrowser.Search;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>
/// Lines a picked local folder up against a remote one and lets the user tick what to upload over it. It reads the remote
/// side itself, through the delegate passed in, and closes with the local files to upload, each renamed to its path
/// below the remote folder. It never deletes anything on the server.
/// </summary>
public sealed partial class FolderCompareDialog : ComponentBase, IDisposable
{
	private const string DifferencesFilter = "differences";
	private const string AllFilter = "all";

	// Beyond this many rows the table renders only the rows on screen.
	private const int VirtualizeThreshold = 200;

	// A dense row holding a small check box.
	private const float RowHeight = 29;

	private static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(150);

	private static readonly FolderCompareStatus[] StatusOrder =
	[
		FolderCompareStatus.OnlyLocal,
		FolderCompareStatus.LocalNewer,
		FolderCompareStatus.SizeDiffers,
		FolderCompareStatus.RemoteNewer,
		FolderCompareStatus.OnlyRemote,
		FolderCompareStatus.Same,
	];

	private static readonly Func<FolderCompareRow, object> RowKey = row => row.RelativePath;
	private static readonly Func<FolderCompareRow, object?> PathField = row => row.RelativePath;
	private static readonly Func<FolderCompareRow, object?> StatusField = row => row.Status;

	private readonly CancellationTokenSource _lifetime = new();
	private readonly HashSet<string> _checked = new(StringComparer.Ordinal);
	private Dictionary<string, LocalFileItem> _local = new(StringComparer.Ordinal);
	private Dictionary<string, RemoteFileEntry> _remote = new(StringComparer.Ordinal);
	private LatestProgress<RemoteSearchProgress> _progress = new();
	private RemoteSearchProgress? _walk;
	private CompareState _state;
	private string? _error;
	private bool _ignoreTimes;
	private bool _showAll;
	private List<FolderCompareRow> _rows = [];
	private IReadOnlyList<FolderCompareRow> _shown = [];
	private Dictionary<FolderCompareStatus, int> _counts = [];
	private HashSet<string> _sameContent = new(StringComparer.Ordinal);
	private int _contentCandidates;
	private bool _contentChecking;
	private int _contentDone;
	private int _contentTotal;
	private string? _contentError;
	private bool _disposed;

	private enum CompareState
	{
		Reading,
		Failed,
		Ready,
	}

	/// <summary>Counts finished checks straight into the dialog; the redraw timer shows them.</summary>
	private sealed class InlineCount(FolderCompareDialog dialog) : IProgress<int>
	{
		public void Report(int value) => Volatile.Write(ref dialog._contentDone, value);
	}

	[CascadingParameter]
	public MokaDialogContext? Dialog { get; set; }

	/// <summary>What the folder picker returned: the folder and everything inside it.</summary>
	[Parameter, EditorRequired]
	public IReadOnlyList<LocalFileItem> LocalItems { get; set; } = [];

	/// <summary>The picked folder's own name.</summary>
	[Parameter]
	public string LocalName { get; set; } = "";

	/// <summary>The remote folder the local one is compared with and uploaded into.</summary>
	[Parameter]
	public string RemoteFolder { get; set; } = RemotePath.Root;

	/// <summary><c>user@host:/path</c> of the remote folder, for the heading.</summary>
	[Parameter]
	public string Destination { get; set; } = "";

	/// <summary>The upload runs as root.</summary>
	[Parameter]
	public bool AsRoot { get; set; }

	/// <summary>Walks the remote folder for every file in it, reporting the walk to the progress it is given.</summary>
	[Parameter, EditorRequired]
	public Func<IProgress<RemoteSearchProgress>, CancellationToken, IAsyncEnumerable<RemoteFileEntry>>? ReadRemote { get; set; }

	/// <summary>
	/// Hashes both copies of each row and returns the relative paths whose contents match, reporting how many it has done.
	/// Null when the server cannot compute checksums, which hides the button.
	/// </summary>
	[Parameter]
	public Func<IReadOnlyList<(string Path, LocalFileItem Local, RemoteFileEntry Remote)>, IProgress<int>, CancellationToken, Task<IReadOnlySet<string>>>? CheckContents { get; set; }

	private FolderCompareOptions Options => FolderCompareOptions.Default with { IgnoreTimes = _ignoreTimes, SameContent = _sameContent };

	private string ContentButtonText => _contentChecking
		? string.Create(CultureInfo.CurrentCulture, $"Checking {_contentDone:N0} of {_contentTotal:N0}")
		: "Check contents of " + EntryVisuals.Count(_contentCandidates, "file", "files");

	private string FilterValue => _showAll ? AllFilter : DifferencesFilter;

	private string ReadingText
	{
		get
		{
			RemoteSearchProgress? progress = _progress.Latest;
			string? files = progress is { Found: > 0 } ? EntryVisuals.Count(progress.Found, "file", "files") : null;
			return WalkStatus.Running("Reading", RemoteFolder, progress, files);
		}
	}

	private string? IncompleteNote => _walk is { } walk && !WalkStatus.IsComplete(walk)
		? WalkStatus.Notes(walk, FolderComparison.RemoteQuery, "file") + " Files the walk did not reach show as only local and are not ticked."
		: null;

	private string RemoteCountText => _state == CompareState.Ready ? EntryVisuals.Count(_remote.Count, "file", "files") : "";

	private string SameText => _counts.GetValueOrDefault(FolderCompareStatus.Same) is > 0 and var same
		? EntryVisuals.Count(same, "file is", "files are") + " the same on both sides."
		: "Both folders are empty.";

	private IEnumerable<FolderCompareRow> ShownUploadable => _shown.Where(row => row.CanUpload);

	private bool AnyShownUploadable => ShownUploadable.Any();

	private bool AllShownChecked => AnyShownUploadable && ShownUploadable.All(row => _checked.Contains(row.RelativePath));

	private bool SomeShownChecked => ShownUploadable.Any(row => _checked.Contains(row.RelativePath));

	private bool CanUpload => _state == CompareState.Ready && _checked.Count > 0;

	private string UploadText => _state == CompareState.Ready && _checked.Count > 0
		? "Upload " + EntryVisuals.Count(_checked.Count, "file", "files")
		: "Upload";

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_lifetime.Cancel();
		_lifetime.Dispose();
	}

	protected override void OnInitialized() => _local = FolderComparison.LocalFiles(LocalItems);

	protected override Task OnInitializedAsync() => ReadAsync();

	private async Task ReadAsync()
	{
		if (_disposed || ReadRemote is not { } readRemote)
		{
			return;
		}

		CancellationToken token = _lifetime.Token;
		using CancellationTokenSource redraw = new();
		_state = CompareState.Reading;
		_error = null;
		_walk = null;
		_progress = new LatestProgress<RemoteSearchProgress>();
		Task redrawing = RedrawWhileReadingAsync(redraw.Token);
		List<RemoteFileEntry> found = [];
		try
		{
			await foreach (RemoteFileEntry entry in readRemote(_progress, token))
			{
				found.Add(entry);
			}

			_walk = _progress.Latest;
			_remote = FolderComparison.RemoteFiles(RemoteFolder, found);
			Classify();
			_state = CompareState.Ready;
		}
		catch (OperationCanceledException)
		{
			// The dialog closed, or the connection went away and the browser stopped the walk.
			_state = CompareState.Failed;
			_error = "Reading stopped before the end.";
		}
		catch (Exception ex)
		{
			_state = CompareState.Failed;
			_error = RemoteErrorText.Describe(ex);
		}
		finally
		{
			await redraw.CancelAsync();
			await redrawing;
		}
	}

	private async Task RedrawWhileReadingAsync(CancellationToken cancellationToken)
	{
		using PeriodicTimer timer = new(RenderInterval);
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				if (_disposed)
				{
					return;
				}

				StateHasChanged();
			}
		}
		catch (OperationCanceledException)
		{
			// The walk is over and renders its own end.
		}
	}

	/// <summary>Compares again and ticks what the rules tick, which drops the user's own ticks.</summary>
	private void Classify()
	{
		FolderCompareOptions options = Options;
		bool remoteComplete = _walk is { } walk && WalkStatus.IsComplete(walk);
		_rows = FolderComparison.Compare(_local, _remote, options);
		_checked.Clear();
		Dictionary<FolderCompareStatus, int> counts = [];
		foreach (FolderCompareRow row in _rows)
		{
			counts[row.Status] = counts.GetValueOrDefault(row.Status) + 1;
			if (FolderComparison.IsPreselected(row, options, remoteComplete))
			{
				_checked.Add(row.RelativePath);
			}
		}

		_counts = counts;
		_contentCandidates = _ignoreTimes ? 0 : FolderComparison.ContentCandidates(_rows).Count;
		ApplyFilter();
	}

	/// <summary>Settles the rows that only differ in time by hashing both copies, then compares again.</summary>
	private async Task CheckContentsAsync()
	{
		if (CheckContents is not { } check || _contentChecking || _disposed)
		{
			return;
		}

		List<FolderCompareRow> candidates = FolderComparison.ContentCandidates(_rows);
		if (candidates.Count == 0)
		{
			return;
		}

		_contentChecking = true;
		_contentError = null;
		_contentDone = 0;
		_contentTotal = candidates.Count;
		using CancellationTokenSource redraw = new();
		Task redrawing = RedrawWhileReadingAsync(redraw.Token);
		try
		{
			IReadOnlySet<string> same = await check([.. candidates.Select(row => (row.RelativePath, row.Local!, row.Remote!))], new InlineCount(this), _lifetime.Token);
			_sameContent.UnionWith(same);
			Classify();
		}
		catch (OperationCanceledException)
		{
			// The dialog closed.
		}
		catch (Exception ex)
		{
			_contentError = "The check stopped: " + RemoteErrorText.Describe(ex);
		}
		finally
		{
			_contentChecking = false;
			await redraw.CancelAsync();
			await redrawing;
		}
	}

	private void ApplyFilter() =>
		_shown = _showAll ? _rows : [.. _rows.Where(row => row.Status != FolderCompareStatus.Same)];

	private void OnFilterChanged(string value)
	{
		_showAll = value == AllFilter;
		ApplyFilter();
	}

	private void OnIgnoreTimesChanged(bool value)
	{
		_ignoreTimes = value;
		Classify();
	}

	private void SetChecked(FolderCompareRow row, bool value)
	{
		if (!row.CanUpload)
		{
			return;
		}

		if (value)
		{
			_checked.Add(row.RelativePath);
		}
		else
		{
			_checked.Remove(row.RelativePath);
		}
	}

	private void SetAllShown(bool value)
	{
		foreach (FolderCompareRow row in ShownUploadable)
		{
			SetChecked(row, value);
		}
	}

	private void Upload()
	{
		if (!CanUpload)
		{
			return;
		}

		List<LocalFileItem> items = FolderComparison.UploadItems(_rows.Where(row => _checked.Contains(row.RelativePath)));
		if (items.Count > 0)
		{
			Dialog?.Close(items);
		}
	}

	private void Cancel() => Dialog?.Cancel();

	private static string CountText(FolderCompareStatus status, int count) =>
		string.Create(CultureInfo.CurrentCulture, $"{count:N0} {StatusLabel(status).ToLowerInvariant()}");

	private static string StatusLabel(FolderCompareStatus status) => status switch
	{
		FolderCompareStatus.OnlyLocal => "Only local",
		FolderCompareStatus.OnlyRemote => "Only on server",
		FolderCompareStatus.LocalNewer => "Local newer",
		FolderCompareStatus.RemoteNewer => "Server newer",
		FolderCompareStatus.SizeDiffers => "Size differs",
		_ => "Same",
	};

	private static MokaColor StatusColor(FolderCompareStatus status) => status switch
	{
		FolderCompareStatus.OnlyLocal or FolderCompareStatus.LocalNewer => MokaColor.Primary,
		FolderCompareStatus.SizeDiffers or FolderCompareStatus.RemoteNewer => MokaColor.Warning,
		_ => MokaColor.Surface,
	};

	private static string StatusClass(FolderCompareStatus status) => status switch
	{
		FolderCompareStatus.OnlyLocal or FolderCompareStatus.LocalNewer => "fc-cell fc-status fc-status--upload",
		FolderCompareStatus.SizeDiffers or FolderCompareStatus.RemoteNewer => "fc-cell fc-status fc-status--check",
		_ => "fc-cell fc-status",
	};

	private static string LocalSize(LocalFileItem local) => local.Length is { } length ? DisplayFormat.Bytes(length) : "";

	// The columns show minutes; a copy newer by seconds needs the seconds to make sense.
	private static string? ExactTime(DateTimeOffset? value) =>
		value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
