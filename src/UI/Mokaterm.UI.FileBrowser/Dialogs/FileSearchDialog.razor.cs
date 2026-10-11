using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Red.Feedback.Dialog;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Search;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>
/// Finds files and folders by name below a remote folder. Results stream into the table while the walk runs; choosing one
/// (double click, Enter or Go to) closes the dialog with that entry for the browser to show. The server is only reached
/// through the delegates passed in, because a dialog must not open other dialogs.
/// </summary>
public sealed partial class FileSearchDialog : ComponentBase, IDisposable
{
	private const string AllKinds = "all";
	private const string FileKinds = "files";
	private const string FolderKinds = "folders";

	// Beyond this many rows the table renders only the rows on screen.
	private const int VirtualizeThreshold = 200;

	// A dense row, as in the browser: the 16px line, 4px cell padding above and below, and the 1px border.
	private const float RowHeight = 25;

	// A walk finds entries far faster than a circuit can carry renders, so the table takes what arrived on this beat
	// rather than one render per match.
	private static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(150);

	private static readonly Func<RemoteFileEntry, object> EntryKey = entry => entry.Path;

	private readonly CancellationTokenSource _lifetime = new();
	private readonly List<RemoteFileEntry> _pending = [];
	private string _pattern = "";
	private string _folder = "";
	private bool _matchCase;
	private bool _includeHidden;
	private RemoteSearchKinds _kinds = RemoteSearchKinds.All;
	private CancellationTokenSource? _run;
	private bool _running;
	private bool _stopped;
	private string? _error;
	private string _root = "";
	private RemoteSearchQuery? _query;
	private LatestProgress<RemoteSearchProgress> _progress = new();
	private IReadOnlyList<RemoteFileEntry> _results = [];
	private RemoteFileEntry? _selected;
	private HashSet<RemoteFileEntry> _selectedItems = [];
	private bool _disposed;

	[CascadingParameter]
	public MokaDialogContext? Dialog { get; set; }

	/// <summary>The header the service's title-less dialog shows, from <see cref="FileBrowserDialogs"/>.</summary>
	[Parameter]
	public DialogHeading? Heading { get; set; }

	/// <summary>The folder the search starts in, which the user can change.</summary>
	[Parameter]
	public string Folder { get; set; } = RemotePath.Root;

	/// <summary>The pattern and options the fields start with. Its caps are not used.</summary>
	[Parameter]
	public RemoteSearchQuery? InitialQuery { get; set; }

	/// <summary>Turns the folder as typed into an absolute path, resolving <c>~</c> and paths relative to the browser's folder.</summary>
	[Parameter, EditorRequired]
	public Func<string, CancellationToken, Task<string>>? ResolveFolder { get; set; }

	/// <summary>Runs a search on the browser's file system: the root, the query, where progress goes.</summary>
	[Parameter, EditorRequired]
	public Func<string, RemoteSearchQuery, IProgress<RemoteSearchProgress>, CancellationToken, IAsyncEnumerable<RemoteFileEntry>>? Search { get; set; }

	private bool CanFind => !_running && _pattern.Trim().Length > 0 && ResolveFolder is not null && Search is not null;

	private string KindsValue => _kinds switch
	{
		RemoteSearchKinds.Files => FileKinds,
		RemoteSearchKinds.Folders => FolderKinds,
		_ => AllKinds,
	};

	private string StatusText
	{
		get
		{
			RemoteSearchProgress? progress = _progress.Latest;
			if (_running)
			{
				string? found = _results.Count > 0 ? EntryVisuals.Count(_results.Count, "found", "found") : null;
				return WalkStatus.Running("Searching", _root, progress, found);
			}

			if (_query is null || progress is null)
			{
				return "A name with * or ? has to match whole; anything else matches part of a name.";
			}

			string summary = WalkStatus.Found(_results.Count, progress.FoldersVisited) + ".";
			if (_stopped)
			{
				return "Stopped. " + summary;
			}

			string notes = WalkStatus.Notes(progress, _query, "result");
			return notes.Length == 0 ? summary : summary + " " + notes;
		}
	}

	private string EmptyTitle => _running ? "Searching..." : _query is null ? "Nothing searched yet" : "Nothing found";

	private string? EmptyDescription => _running || _query is not null ? null : "Type a name and press Enter.";

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

	protected override void OnInitialized()
	{
		_folder = Folder;
		_root = Folder;
		if (InitialQuery is { } initial)
		{
			_pattern = initial.Pattern;
			_matchCase = initial.MatchCase;
			_includeHidden = initial.IncludeHidden;
			_kinds = initial.Kinds == RemoteSearchKinds.None ? RemoteSearchKinds.All : initial.Kinds;
		}
	}

	private void OnPatternChanged(string value) => _pattern = value;

	private void OnFolderChanged(string value) => _folder = value;

	private void OnKindsChanged(string value) => _kinds = value switch
	{
		FileKinds => RemoteSearchKinds.Files,
		FolderKinds => RemoteSearchKinds.Folders,
		_ => RemoteSearchKinds.All,
	};

	private async Task FindAsync()
	{
		if (_disposed || !CanFind || ResolveFolder is not { } resolveFolder || Search is not { } search)
		{
			return;
		}

		using CancellationTokenSource run = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		using CancellationTokenSource redraw = new();
		Task redrawing = Task.CompletedTask;
		RemoteSearchQuery query = new()
		{
			Pattern = _pattern.Trim(),
			MatchCase = _matchCase,
			IncludeHidden = _includeHidden,
			Kinds = _kinds,
		};

		string typed = _folder.Trim();
		_run = run;
		_running = true;
		_stopped = false;
		_error = null;
		_query = query;
		_root = typed.Length > 0 ? typed : Folder;
		_progress = new LatestProgress<RemoteSearchProgress>();
		_pending.Clear();
		_results = [];
		_selected = null;
		_selectedItems = [];
		try
		{
			string root;
			try
			{
				root = await resolveFolder(typed, run.Token);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_error = "Could not open " + typed + ". " + RemoteErrorText.Describe(ex);
				return;
			}

			_root = root;
			_folder = root;
			redrawing = RedrawWhileRunningAsync(redraw.Token);
			await foreach (RemoteFileEntry entry in search(root, query, _progress, run.Token))
			{
				_pending.Add(entry);
			}
		}
		catch (OperationCanceledException)
		{
			// Stop, a choice made while the walk ran, the dialog closing or the connection going away.
			_stopped = true;
		}
		catch (Exception ex)
		{
			_error = "Could not search " + _root + ". " + RemoteErrorText.Describe(ex);
		}
		finally
		{
			await redraw.CancelAsync();
			await redrawing;
			Publish();
			_running = false;
			_run = null;
		}
	}

	private async Task RedrawWhileRunningAsync(CancellationToken cancellationToken)
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

				Publish();
				StateHasChanged();
			}
		}
		catch (OperationCanceledException)
		{
			// The search is over and renders its own end.
		}
	}

	// The table compares its items by reference, so every batch hands it a new list.
	private void Publish()
	{
		if (_pending.Count > 0)
		{
			_results = [.. _results, .. _pending];
			_pending.Clear();
		}
	}

	private void Stop() => _run?.Cancel();

	private void Select(RemoteFileEntry entry)
	{
		_selected = entry;
		_selectedItems = [entry];
	}

	// The table turns Enter on a cell into a click on its row, which selects it before this runs.
	private void OnResultsKeyDown(KeyboardEventArgs args)
	{
		if (args.Key == "Enter" && !args.Repeat && !args.CtrlKey && !args.AltKey && !args.MetaKey && !args.ShiftKey)
		{
			ChooseSelected();
		}
	}

	private void ChooseSelected()
	{
		if (_selected is { } entry)
		{
			Choose(entry);
		}
	}

	private void Choose(RemoteFileEntry entry)
	{
		_run?.Cancel();
		Dialog?.Close(entry);
	}

	private void Close() => Dialog?.Cancel();

	/// <summary>Where an entry is below the search root: <c>.</c> for the root itself.</summary>
	private string FolderText(RemoteFileEntry entry)
	{
		string parent = RemotePath.GetParent(entry.Path);
		return RemotePath.GetRelativePath(_root, parent) switch
		{
			null => parent,
			"" => ".",
			{ } relative => relative,
		};
	}
}
