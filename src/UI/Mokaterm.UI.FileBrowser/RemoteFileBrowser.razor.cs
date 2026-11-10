using Moka.Red.ContextMenu;
using Moka.Red.Core.Enums;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moka.Red.Core.Icons;
using Moka.Red.Data.FileBrowser;
using Moka.Red.Feedback.Dialog;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Files;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Dialogs;
using Mokaterm.UI.FileBrowser.Interop;
using Mokaterm.UI.FileBrowser.Transfers;

namespace Mokaterm.UI.FileBrowser;

/// <summary>
/// Remote file browser for any session exposing <see cref="Abstractions.FileSystem.IFileSystemFeature"/>: listing,
/// navigation, context menus, user/root switching, drag and drop uploads and downloads through the transfer queue.
/// Re-opens the file system when the session reconnects. The same browser shows a file system a module hands over
/// (<see cref="Files"/>), which <see cref="IFileBrowserDialog"/> puts in a dialog.
/// </summary>
public partial class RemoteFileBrowser : ComponentBase, IAsyncDisposable
{
	// Widths in CSS pixels where the browser reaches BrowserWidth.Medium, Wide and Roomy. Medium fits the name, size and
	// modified columns, Wide every column, each at its set width, and Roomy the details pane beside a Medium list.
	private static readonly int[] WidthSteps = [440, 740, 800];

	// Moka's details pane is 17rem wide.
	private const int DetailsPanePixels = 17 * 16;

	private static readonly MokaFileFitWidths ColumnFit = new(Medium: WidthSteps[0] / 16.0, Wide: WidthSteps[1] / 16.0);

	// Moka fits the columns to the whole browser, so beside the pane they wait for the room the pane takes as well.
	private static readonly MokaFileFitWidths ColumnFitBesideDetails =
		new(Medium: (WidthSteps[0] + DetailsPanePixels) / 16.0, Wide: (WidthSteps[1] + DetailsPanePixels) / 16.0);

	private static readonly IReadOnlySet<string> NothingSelected = new HashSet<string>(StringComparer.Ordinal);

	private const string UserMode = "user";
	private const string RootMode = "root";

	private static readonly string[] InitialDirectoryOptions = ["ssh.initialDirectory", "ftp.initialDirectory"];

	private readonly CancellationTokenSource _lifetime = new();

	private ElementReference _root;
	private DotNetObjectReference<FileBrowserJsReceiver>? _receiver;
	private IJSObjectReference? _jsHandle;
	private IAsyncDisposable? _dropRegistration;
	private FileBrowserDialogs? _dialogs;
	private bool _activeSentToJs;
	private bool _disposed;

	private BrowserSource? _source;
	private object? _live;
	private IFileSystemFeature? _feature;
	private IRemoteFileSystem? _userFileSystem;
	private IRemoteFileSystem? _rootFileSystem;
	private IRemoteFileSystem? _fileSystem;
	private CancellationTokenSource? _connection;
	private bool _opening;
	private string? _openError;

	private string _path = RemotePath.Root;
	private bool _loaded;
	private bool _loadingStart;
	private IReadOnlyList<RemoteFileEntry> _entries = [];
	private DirectoryView _view = DirectoryView.Empty;
	private IReadOnlySet<string> _selected = NothingSelected;
	private MokaFileView _sort = new();
	private bool _showHidden;
	private bool _detailsOpen;
	private string _filter = "";

	private MokaFileBrowser<RemoteFileEntry>? _browser;
	private MokaFileBrowserText _text = new();
	private int _mount;

	// The listing that decided the start folder, for the browser mounted on it to show without reading it again.
	private (string Folder, IReadOnlyList<RemoteFileEntry> Entries)? _firstListing;

	// What a listing of another folder brought, until the browser says it moved there.
	private IReadOnlyList<RemoteFileEntry>? _arrivingListing;
	private PendingNavigation? _navigation;
	private string? _refreshSelect;

	private BrowserAlert? _alert;
	private int _busy;
	private bool _switchingMode;
	private bool _dragOver;
	private bool _editingPath;
	private string? _renamingPath;
	private BrowserWidth _width = BrowserWidth.Narrow;
	private string _pathText = "";

	// Work for the next OnAfterRenderAsync, once the DOM matches the new state.
	private bool _restoreFocus;
	private string? _focusSelector;

	/// <summary>The session tab whose files to show. Exactly one of this and <see cref="Files"/> is set.</summary>
	[Parameter]
	public ISessionHandle? Session { get; set; }

	/// <summary>A file system a module handed over, shown instead of a session's. Exactly one of this and <see cref="Session"/> is set.</summary>
	[Parameter]
	public FileBrowserRequest? Files { get; set; }

	/// <summary>True while visible. Hidden browsers stop polling and ignore keyboard shortcuts.</summary>
	[Parameter]
	public bool IsActive { get; set; } = true;

	/// <summary>Starting directory. Null starts in the user's home directory.</summary>
	[Parameter]
	public string? InitialPath { get; set; }

	/// <summary>Told the folder on show, as an absolute path, each time a listing of another folder came in.</summary>
	[Parameter]
	public EventCallback<string> FolderChanged { get; set; }

	[Parameter]
	public string? Class { get; set; }

	[Parameter]
	public string? Style { get; set; }

	[Inject]
	private IMokaDialogService DialogService { get; set; } = default!;

	[Inject]
	private IMokaContextMenuService ContextMenu { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private ILocalFileAccess LocalFiles { get; set; } = default!;

	[Inject]
	private IFileDropBridge DropBridge { get; set; } = default!;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	[Inject]
	private FileBrowserInterop Interop { get; set; } = default!;

	[Inject]
	private RemoteTransferService Transfers { get; set; } = default!;

	[Inject]
	private FileBrowserViewMemory ViewMemory { get; set; } = default!;

	[Inject]
	private ILogger<RemoteFileBrowser> Logger { get; set; } = default!;

	private FileBrowserDialogs Dialogs => _dialogs ??= new FileBrowserDialogs(DialogService);

	private CancellationToken ConnectionToken =>
		_disposed ? new CancellationToken(canceled: true) : _connection?.Token ?? _lifetime.Token;

	private BrowserSource Source => _source ?? throw new InvalidOperationException("The file browser has no files to show yet.");

	/// <summary>A file system is open and a listing is on screen.</summary>
	private bool CanBrowse => _fileSystem is not null && _loaded;

	private bool IsFirstListing => _fileSystem is not null && !_loaded && _loadingStart;

	private bool ShowsList => CanBrowse || IsFirstListing;

	private bool IsFiltering => _filter.Trim().Length > 0;

	private bool OnlyHiddenEntries => !IsFiltering && _entries.Count > 0;

	private IEnumerable<RemotePathSegment> FolderSegments => RemotePath.GetSegments(_path).Skip(1);

	private string RootCrumbClass => RemotePath.IsRoot(_path) ? "fb-crumb fb-crumb--root fb-crumb--current" : "fb-crumb fb-crumb--root";

	private bool IsElevated => _fileSystem?.IsElevated == true;

	private bool CanElevate => _feature?.SupportsElevation == true;

	// The browser draws its own bar for the folders it loads; this one is for everything else.
	private bool IsBusy => _loadingStart || _opening || _switchingMode || _busy > 0;

	private bool ShowHidden => Settings.Get<FileTransferSettings>().ShowHiddenFiles;

	// While root is being opened (and the sudo prompt may be up) the control already shows the requested mode.
	private string ModeValue => IsElevated || _switchingMode ? RootMode : UserMode;

	private TransferOrigin Origin => new(Source.Group, Source.Location);

	private List<RemoteFileEntry> SelectedEntries => [.. _view.Entries.Where(entry => _selected.Contains(entry.Path))];

	private IReadOnlyList<MokaFileCrumb> Crumbs => [.. RemotePath.GetSegments(_path).Select(segment => new MokaFileCrumb(segment.Path, segment.Name))];

	private MokaFileControls ShownControls =>
		MokaFileControls.Back | MokaFileControls.Up | MokaFileControls.Path | MokaFileControls.Refresh | (IsNarrow ? MokaFileControls.None : MokaFileControls.Forward);

	// The rest of the keys are in filebrowser.js, which needs them with other modifiers than the browser's or for operations of ours.
	private MokaFileKeys TakenKeys => CanBrowse
		? MokaFileKeys.Move | MokaFileKeys.Toggle | MokaFileKeys.SelectAll | MokaFileKeys.History | MokaFileKeys.Filter
		: MokaFileKeys.None;

	private string RootClass => $"fb{(IsElevated ? " fb--root" : "")} {Class}".TrimEnd();

	private string AccountLabel => _fileSystem is null ? "" : _fileSystem.UserName + "@" + Source.Location;

	private string DropAccountText => "As " + _fileSystem?.UserName;

	private string NoMatchesText => "Nothing here matches \"" + _filter.Trim() + "\".";

	private string HiddenOnlyText => _entries.Count == 1
		? "This folder holds 1 hidden item."
		: string.Create(CultureInfo.CurrentCulture, $"This folder holds {_entries.Count:N0} hidden items.");

	private bool IsNarrow => _width == BrowserWidth.Narrow;

	private bool IsTiles => _sort.Mode == MokaFileViewMode.Tiles;

	private bool CanShowDetails => _width >= BrowserWidth.Roomy;

	// A narrower browser keeps the pane closed rather than move it into Moka's drawer, which opens at the window's edge
	// however far from it the file panel is. Whether it was open is kept for when the browser is wide again.
	private bool ShowsDetails => _detailsOpen && CanShowDetails && ShowsList;

	private MokaFileFitWidths ColumnFitNow => ShowsDetails ? ColumnFitBesideDetails : ColumnFit;

	private string TilesTitle => IsTiles ? "Show as a list" : "Show as tiles";

	private string ItemCountText
	{
		get
		{
			if (!CanBrowse)
			{
				return "";
			}

			string shown = _view.Count == 1 ? "1 item" : string.Create(CultureInfo.CurrentCulture, $"{_view.Count:N0} items");
			int notShown = _entries.Count - _view.Count;
			return notShown > 0 ? string.Create(CultureInfo.CurrentCulture, $"{shown}, {notShown:N0} not shown") : shown;
		}
	}

	private string SelectionText
	{
		get
		{
			List<RemoteFileEntry> selected = SelectedEntries;
			long bytes = selected.Where(entry => entry.Kind == RemoteEntryKind.File).Sum(entry => entry.Size);
			return string.Create(CultureInfo.CurrentCulture, $"{selected.Count:N0} selected, {DisplayFormat.Bytes(bytes)}");
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_source?.Unsubscribe(OnSessionChanged);

		Settings.Changed -= OnSettingsChanged;
		Bookmarks.Changed -= OnBookmarksChanged;
		CancelConnection();
		_lifetime.Cancel();
		CloseAlert();

		await Interlocked.Exchange(ref _jsHandle, null).ReleaseAsync();
		if (Interlocked.Exchange(ref _dropRegistration, null) is { } registration)
		{
			try
			{
				await registration.DisposeAsync();
			}
			catch (Exception ex) when (JsModule.IsExpected(ex))
			{
				// The page is gone and its drop listeners with it.
			}
		}

		Interlocked.Exchange(ref _receiver, null)?.Dispose();
		_lifetime.Dispose();
		GC.SuppressFinalize(this);
	}

	protected override void OnInitialized()
	{
		_receiver = DotNetObjectReference.Create(new FileBrowserJsReceiver(this));
		Settings.Changed += OnSettingsChanged;
		Bookmarks.Changed += OnBookmarksChanged;
		_text = new MokaFileBrowserText
		{
			Label = "Remote files",
			ItemCount = _ => ItemCountText,
			FilteredCount = (_, _) => ItemCountText,
			SelectedCount = _ => SelectionText,
			NoMatches = _ => NoMatchesText,
		};
		_sort = ViewMemory.View;
		_detailsOpen = ViewMemory.DetailsOpen;
		ReadViewSettings();
	}

	protected override void OnParametersSet()
	{
		ApplyFollowParameters();
		object key = (Session, Files) switch
		{
			({ } session, null) => session,
			(null, { } files) => files,
			_ => throw new InvalidOperationException("Give the file browser either a Session or Files, and not both."),
		};

		if (ReferenceEquals(key, _source?.Key))
		{
			return;
		}

		_source?.Unsubscribe(OnSessionChanged);
		_source = Session is { } tab ? BrowserSource.For(tab) : BrowserSource.For(Files!);
		_source.Subscribe(OnSessionChanged);
		ResetForSession();

		// A prerender has no circuit to hold a connection for; the interactive render opens the file system.
		if (RendererInfo.IsInteractive)
		{
			_ = RunSafeAsync(SyncConnectionAsync);
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender)
		{
			await AttachAsync();
		}

		if (_jsHandle is null)
		{
			return;
		}

		if (_activeSentToJs != IsActive)
		{
			_activeSentToJs = IsActive;
			await _jsHandle.TryInvokeVoidAsync("setActive", IsActive);
		}

		if (_focusSelector is { } selector)
		{
			_focusSelector = null;
			await _jsHandle.TryInvokeVoidAsync("focus", selector);
		}

		if (_restoreFocus)
		{
			_restoreFocus = false;
			await _jsHandle.TryInvokeVoidAsync("restoreFocus");
		}
	}

	private async Task AttachAsync()
	{
		if (_receiver is null || _disposed)
		{
			return;
		}

		try
		{
			IJSObjectReference handle = await Interop.AttachBrowserAsync(_root, _receiver, IsActive, WidthSteps, CancellationToken.None);
			if (_disposed)
			{
				await handle.ReleaseAsync();
				return;
			}

			_jsHandle = handle;
			_activeSentToJs = IsActive;
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			Logger.LogDebug(ex, "Keyboard shortcuts could not be attached to the file browser");
		}

		if (_disposed)
		{
			return;
		}

		try
		{
			FileDropHandlers handlers = new() { OnDrop = OnFilesDroppedAsync, OnDragOver = OnDragOverChangedAsync };
			IAsyncDisposable registration = await DropBridge.AttachAsync(_root, handlers, CancellationToken.None);
			if (_disposed)
			{
				await registration.DisposeAsync();
				return;
			}

			_dropRegistration = registration;
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			Logger.LogDebug(ex, "File drops could not be attached to the file browser");
		}
	}

	/// <summary>Runs work on the renderer from a service event or a fire-and-forget call, logging what fails.</summary>
	private async Task RunSafeAsync(Func<Task> work)
	{
		try
		{
			await InvokeAsync(work);
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The circuit or WebView is closing.
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "The file browser failed to update");
		}
	}

	private void OnSessionChanged() => _ = RunSafeAsync(SyncConnectionAsync);

	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey != FileTransferSettings.SectionKey)
		{
			return;
		}

		_ = RunSafeAsync(() =>
		{
			if (!_disposed)
			{
				RebuildView();
				StateHasChanged();
			}

			return Task.CompletedTask;
		});
	}

	private void ResetForSession()
	{
		CancelConnection();
		_live = null;
		_feature = null;
		_userFileSystem = null;
		_rootFileSystem = null;
		_fileSystem = null;
		_opening = false;
		_openError = null;
		_path = RemotePath.Root;
		_loaded = false;
		_loadingStart = false;
		_entries = [];
		_filter = "";
		_editingPath = false;
		_dragOver = false;
		_selected = NothingSelected;
		_firstListing = null;
		_arrivingListing = null;
		_mount++;
		_clipboard = null;
		_pendingTerminalFolder = null;
		_terminalHome = null;
		_bookmarks = [];
		_bookmarksLoaded = false;
		RebuildView();
		CloseAlert();
	}

	private void CancelConnection()
	{
		if (_connection is not null)
		{
			_connection.Cancel();
			_connection.Dispose();
			_connection = null;
		}
	}

	/// <summary>Follows the session: a new live connection (connect or reconnect) reopens the file system.</summary>
	private async Task SyncConnectionAsync()
	{
		if (_disposed)
		{
			return;
		}

		object? live = Source.Connection;
		if (ReferenceEquals(live, _live))
		{
			StateHasChanged();
			return;
		}

		// The file systems of the old connection belong to the session, which disposes them; only forget them here.
		CancelConnection();
		_live = live;
		_feature = live is null ? null : Source.FeatureOf(live);
		_userFileSystem = null;
		_rootFileSystem = null;
		_fileSystem = null;
		_terminalHome = null;
		_opening = false;
		_openError = null;
		_loadingStart = false;
		_dragOver = false;
		_editingPath = false;
		_renamingPath = null;
		StateHasChanged();

		// A listing that was on its way waits to be replaced, since the connection it needed is gone.
		if (_browser is { IsBusy: true } browser)
		{
			await browser.ReloadAsync();
		}

		if (live is not null && _feature is not null)
		{
			_connection = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
			await OpenFileSystemAsync();
		}
	}

	private async Task OpenFileSystemAsync()
	{
		if (_feature is not { } feature || _connection is null)
		{
			return;
		}

		CancellationToken token = _connection.Token;
		_opening = true;
		_openError = null;
		StateHasChanged();

		IRemoteFileSystem fileSystem;
		try
		{
			fileSystem = await feature.OpenAsync(token);
		}
		catch (Exception ex)
		{
			if (!token.IsCancellationRequested)
			{
				Logger.LogWarning("Opening the remote file system failed: {Error}", LogSafe.Describe(ex));
				_opening = false;
				_openError = RemoteErrorText.Describe(ex);
				StateHasChanged();
			}

			return;
		}

		if (token.IsCancellationRequested)
		{
			return;
		}

		_opening = false;
		_userFileSystem = fileSystem;
		_fileSystem = fileSystem;
		if (!_bookmarksLoaded)
		{
			_ = RunSafeAsync(LoadBookmarksAsync);
		}

		// A reconnect keeps the folder the user was in; the first connection starts where the settings say.
		if (_loaded)
		{
			await RefreshAsync();

			// The browser dropped the selection while the list was hidden.
			_selected = new HashSet<string>(_selected, StringComparer.Ordinal);
			StateHasChanged();
		}
		else
		{
			await LoadStartAsync();
		}
	}

	private async Task RetryOpenAsync()
	{
		if (_fileSystem is null)
		{
			await OpenFileSystemAsync();
		}
		else if (!_loaded)
		{
			await LoadStartAsync();
		}
	}

	/// <summary>Opens <see cref="InitialPath"/>, else the connection's initial directory option, else the home folder.</summary>
	private async Task LoadStartAsync()
	{
		if (_fileSystem is not { } fileSystem)
		{
			return;
		}

		CancellationToken token = ConnectionToken;
		if (ConfiguredStartPath is { } configured)
		{
			string? resolved = null;
			try
			{
				resolved = await ResolveTypedPathAsync(fileSystem, configured, RemotePath.Root, token);
			}
			catch (Exception ex)
			{
				if (token.IsCancellationRequested)
				{
					return;
				}

				ShowAlert("Could not open " + configured, ex, retryAsRoot: null);
			}

			if (resolved is { } startPath)
			{
				if (await ListStartAsync(fileSystem, startPath) is not { } startError)
				{
					return;
				}

				ShowAlert("Could not open " + startPath, startError, _ => NavigateAsync(startPath));
			}
		}

		string home;
		try
		{
			home = await fileSystem.GetHomeDirectoryAsync(token);
		}
		catch (Exception ex)
		{
			if (token.IsCancellationRequested)
			{
				return;
			}

			Logger.LogDebug(ex, "The home folder could not be read; starting at the root");
			home = RemotePath.Root;
		}

		if (await ListStartAsync(fileSystem, home) is { } error)
		{
			ShowAlert("Could not open " + home, error, _ => RefreshAsync());
		}
	}

	private string? ConfiguredStartPath
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(InitialPath))
			{
				return InitialPath.Trim();
			}

			if (Files?.StartFolder is { } startFolder && !string.IsNullOrWhiteSpace(startFolder))
			{
				return startFolder.Trim();
			}

			return Source.ConfiguredStartFolder(InitialDirectoryOptions);
		}
	}

	// The browser is mounted only once this listed: it cannot forget a folder, and one it started on before the real start
	// was known would be offered to Back.
	private async Task<Exception?> ListStartAsync(IRemoteFileSystem fileSystem, string path)
	{
		CancellationToken token = ConnectionToken;
		_loadingStart = true;
		StateHasChanged();
		try
		{
			IReadOnlyList<RemoteFileEntry> entries = await fileSystem.ListAsync(path, token);
			if (token.IsCancellationRequested || !ReferenceEquals(fileSystem, _fileSystem))
			{
				return null;
			}

			_firstListing = (path, entries);
			_entries = entries;
			_path = path;
			_loaded = true;
			_selected = NothingSelected;
			_filter = "";
			RebuildView();
			_mount++;
			if (FolderChanged.HasDelegate)
			{
				await FolderChanged.InvokeAsync(path);
			}

			return null;
		}
		catch (Exception ex)
		{
			return token.IsCancellationRequested || !ReferenceEquals(fileSystem, _fileSystem) ? null : ex;
		}
		finally
		{
			_loadingStart = false;
			StateHasChanged();
		}
	}

	// The failure is shown from here because the browser's own alert cannot say which folder failed or offer root.
	private async Task<IReadOnlyList<RemoteFileEntry>> LoadFolderAsync(string folder, CancellationToken cancellationToken)
	{
		if (_firstListing is { } first && first.Folder == folder)
		{
			_firstListing = null;
			return first.Entries;
		}

		if (_fileSystem is not { } fileSystem || !_loaded)
		{
			return _entries;
		}

		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ConnectionToken);
		try
		{
			IReadOnlyList<RemoteFileEntry> entries = await fileSystem.ListAsync(folder, linked.Token);
			if (folder == _path)
			{
				_entries = entries;
				RebuildView();
			}
			else
			{
				_arrivingListing = entries;
			}

			return entries;
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
		{
			if (ConnectionToken.IsCancellationRequested)
			{
				await Task.Delay(Timeout.Infinite, cancellationToken);
			}

			ReportLoadFailure(folder, ex);
			throw;
		}
	}

	private void ReportLoadFailure(string folder, Exception error)
	{
		if (folder == _path)
		{
			string? select = _refreshSelect;
			ShowAlert("Could not refresh " + folder, error, _ => RefreshAsync(select));
		}
		else if (_navigation is { } navigation && navigation.Folder == folder && !navigation.ReportFailure)
		{
			Logger.LogDebug("A folder the browser was sent to could not be listed: {Error}", LogSafe.Describe(error));
		}
		else
		{
			string? select = _navigation?.Folder == folder ? _navigation.Select : null;
			ShowAlert("Could not open " + folder, error, _ => NavigateAsync(folder, select));
		}
	}

	private async Task OnFolderChangedAsync(string folder)
	{
		_path = folder;
		if (_arrivingListing is { } entries)
		{
			_entries = entries;
			_arrivingListing = null;
		}

		_filter = "";
		_editingPath = false;
		RebuildView();
		CloseAlert();
		_restoreFocus = true;
		if (FolderChanged.HasDelegate)
		{
			await FolderChanged.InvokeAsync(folder);
		}
	}

	private async Task NavigateAsync(string path, string? select = null, bool reportFailure = true)
	{
		if (_fileSystem is null || _browser is not { } browser)
		{
			return;
		}

		string target = RemotePath.Normalize(path);
		_editingPath = false;
		if (target == _path)
		{
			CloseAlert();
			await RefreshAsync(select);
			_restoreFocus = true;
			return;
		}

		_navigation = new PendingNavigation(target, select, reportFailure);
		try
		{
			await browser.NavigateAsync(target, select);
		}
		finally
		{
			_navigation = null;
			_restoreFocus = true;
			StateHasChanged();
		}
	}

	private Task GoUpAsync() =>
		CanBrowse && !RemotePath.IsRoot(_path) ? NavigateAsync(RemotePath.GetParent(_path), select: _path) : Task.CompletedTask;

	private async Task GoHomeAsync()
	{
		if (_fileSystem is not { } fileSystem)
		{
			return;
		}

		CancellationToken token = ConnectionToken;
		string home;
		try
		{
			home = await fileSystem.GetHomeDirectoryAsync(token);
		}
		catch (Exception ex)
		{
			if (!token.IsCancellationRequested)
			{
				ShowAlert("Could not find the home folder", ex, retryAsRoot: null);
			}

			return;
		}

		await NavigateAsync(home);
	}

	/// <summary>Lists the current folder again, keeping the selection.</summary>
	private async Task RefreshAsync(string? select = null)
	{
		if (_fileSystem is null)
		{
			return;
		}

		if (!_loaded)
		{
			await LoadStartAsync();
			return;
		}

		if (_browser is not { } browser)
		{
			return;
		}

		_refreshSelect = select;
		await browser.ReloadAsync();
		_refreshSelect = null;
		if (select is not null)
		{
			await RevealAsync(select);
		}
	}

	private void BeginPathEdit()
	{
		if (!CanBrowse)
		{
			return;
		}

		_pathText = _path;
		_editingPath = true;
		_focusSelector = "[data-path-editor]";
	}

	private void CancelPathEdit()
	{
		if (_editingPath)
		{
			_editingPath = false;
			_restoreFocus = true;
		}
	}

	private void OnPathTextChanged(string value) => _pathText = value;

	private async Task OnPathKeyDownAsync(KeyboardEventArgs args)
	{
		if (args.Key == "Escape")
		{
			CancelPathEdit();
		}
		else if (args.Key == "Enter")
		{
			await SubmitPathAsync();
		}
	}

	/// <summary>Goes to a typed path. <c>~</c> resolves on the server, relative paths start at the current folder, and a file path opens its folder with the file selected.</summary>
	private async Task SubmitPathAsync()
	{
		_editingPath = false;
		if (_fileSystem is not { } fileSystem || string.IsNullOrWhiteSpace(_pathText))
		{
			_restoreFocus = true;
			return;
		}

		CancellationToken token = ConnectionToken;
		string typed = _pathText.Trim();
		string target;
		RemoteFileEntry? entry = null;
		try
		{
			target = await ResolveTypedPathAsync(fileSystem, typed, _path, token);
			entry = await fileSystem.StatAsync(target, token);
		}
		catch (Exception ex)
		{
			if (!token.IsCancellationRequested)
			{
				ShowAlert("Could not open " + typed, ex, retryAsRoot: null);
			}

			return;
		}

		if (entry is { IsDirectoryLike: false } && (entry.Kind == RemoteEntryKind.File || entry.LinkTargetKind == RemoteEntryKind.File))
		{
			await NavigateAsync(RemotePath.GetParent(target), select: target);
		}
		else if (target == _path)
		{
			_restoreFocus = true;
		}
		else
		{
			await NavigateAsync(target);
		}
	}

	private static async Task<string> ResolveTypedPathAsync(IRemoteFileSystem fileSystem, string text, string basePath, CancellationToken cancellationToken)
	{
		string trimmed = text.Trim();
		if (trimmed.StartsWith('~'))
		{
			return RemotePath.Normalize(await fileSystem.ResolvePathAsync(trimmed, cancellationToken));
		}

		return RemotePath.Normalize(trimmed.StartsWith('/') ? trimmed : RemotePath.Combine(basePath, trimmed));
	}

	private void ReadViewSettings()
	{
		FileTransferSettings settings = Settings.Get<FileTransferSettings>();
		_showHidden = settings.ShowHiddenFiles;
		_sort = _sort with { FoldersFirst = settings.FoldersFirst };
	}

	private void RebuildView()
	{
		ReadViewSettings();
		_view = DirectoryView.Build(_entries, _showHidden, _filter, _sort.SortBy, _sort.Descending, _sort.FoldersFirst);

		// Gone from the list (filtered out, deleted, another folder), so nothing can be typed into any more.
		if (_view.IndexOf(_renamingPath) < 0)
		{
			_renamingPath = null;
		}
	}

	// The browser's own reveal would clear a filter hiding the row.
	private Task RevealAsync(string path) =>
		_browser is { } browser && _view.IndexOf(path) >= 0 ? browser.RevealAsync(path) : Task.CompletedTask;

	private void OnFilterChanged(string value)
	{
		_filter = value;
		RebuildView();
	}

	private void OnFilterKeyDown(KeyboardEventArgs args)
	{
		if (args.Key == "Escape")
		{
			_filter = "";
			RebuildView();
			_restoreFocus = true;
		}
	}

	private async Task ToggleHiddenAsync()
	{
		bool show = !ShowHidden;
		try
		{
			await Settings.UpdateAsync<FileTransferSettings>(settings => settings with { ShowHiddenFiles = show });
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "The hidden files setting could not be saved");
		}

		RebuildView();
	}

	// While the list is hidden the browser has dropped every row, and the selection only looks empty.
	private void OnSelectedChanged(IReadOnlySet<string> selected)
	{
		if (ShowsList)
		{
			_selected = selected;
		}
	}

	// A heading's third click puts the natural order back, which is the name going up.
	private void OnViewChanged(MokaFileView view)
	{
		bool thirdClick = view.SortBy == _sort.SortBy && _sort.Descending && !view.Descending;
		_sort = thirdClick ? view with { SortBy = MokaFileSort.Name } : view;
		ViewMemory.View = _sort;
		RebuildView();
	}

	// Also reached from the background menu, whose click belongs to another component.
	private void ToggleTiles()
	{
		_sort = _sort with { Mode = IsTiles ? MokaFileViewMode.List : MokaFileViewMode.Tiles };
		ViewMemory.View = _sort;

		// Tiles draw no name cell, so a field left open in the list would come back with the list.
		_renamingPath = null;
		RebuildView();
		_restoreFocus = true;
		StateHasChanged();
	}

	private void ToggleDetails()
	{
		_detailsOpen = !_detailsOpen;
		ViewMemory.DetailsOpen = _detailsOpen;
		StateHasChanged();
	}

	private bool ShowsEntry(RemoteFileEntry entry) => ShowsList && (_showHidden || !entry.IsHidden);

	private static string EntryPath(RemoteFileEntry entry) => entry.Path;

	// Sizes and times are drawn by the columns, and a size given here would add a folder total to the status bar.
	private static MokaFileInfo Describe(RemoteFileEntry entry) =>
		new(entry.Path, entry.Name, entry.IsDirectoryLike, Icon: EntryVisuals.IconFor(entry), IconColor: EntryVisuals.ColorFor(entry));

	private IReadOnlyList<MokaFileColumn<RemoteFileEntry>> ColumnList
	{
		get
		{
			(bool, bool) features = (HasFeature(RemoteFileSystemFeatures.Permissions), HasFeature(RemoteFileSystemFeatures.Ownership));
			if (_columns is null || features != _columnFeatures)
			{
				_columnFeatures = features;
				List<MokaFileColumn<RemoteFileEntry>> columns =
				[
					Column(EntryOrdering.SizeColumn, "Size", "68px", EntryVisuals.SizeText) with { Align = MokaTextAlign.Right, Fit = MokaFileColumnFit.Always },
					Column(EntryOrdering.ModifiedColumn, "Modified", "124px", entry => DisplayFormat.Timestamp(entry.LastModified)) with { Fit = MokaFileColumnFit.Medium },
				];
				if (features.Item1)
				{
					columns.Add(Column(EntryOrdering.PermissionsColumn, "Permissions", "104px", EntryVisuals.PermissionsText));
				}

				if (features.Item2)
				{
					columns.Add(Column(EntryOrdering.OwnerColumn, "Owner", "112px", EntryOrdering.OwnerLabel));
				}

				_columns = columns;
			}

			return _columns;
		}
	}

	// The comparison falls back to the name itself, which the browser then turns around with the rest for a descending sort.
	private static MokaFileColumn<RemoteFileEntry> Column(string id, string title, string width, Func<RemoteFileEntry, string> text) =>
		new(id, title)
		{
			Cell = CellOf(text),
			Compare = (a, b) => EntryOrdering.Compare(a, b, id, descending: false, foldersFirst: false),
			Width = width,
			Mono = true,
			Align = MokaTextAlign.Left,
		};

	private IReadOnlyList<MokaFileColumn<RemoteFileEntry>>? _columns;
	private (bool, bool) _columnFeatures;

	private bool HasFeature(RemoteFileSystemFeatures feature) => _fileSystem is not null && (_fileSystem.Features & feature) == feature;

	private StateInfo DescribeState()
	{
		if (Source.Session is { State: SessionState.Connecting } connecting)
		{
			return new StateInfo(MokatermIcons.Connect, "Connecting...", connecting.StatusMessage, CanRetry: false);
		}

		// The states a reconnect acts on are the states with no file system behind them.
		if (Source.Session is { } session && ISessionHandle.CanReconnect(session.State))
		{
			return new StateInfo(MokatermIcons.Disconnect, "Disconnected", session.StatusMessage, CanRetry: false);
		}

		if (_live is not null && _feature is null)
		{
			return new StateInfo(MokatermIcons.Folder, "No file access", "This connection does not offer a file system.", CanRetry: false);
		}

		if (_openError is not null)
		{
			return new StateInfo(MokaIcons.Status.Warning, "Could not open files", _openError, CanRetry: true);
		}

		if (_fileSystem is null || _opening || _loadingStart)
		{
			return new StateInfo(MokatermIcons.FolderOpen, "Opening files...", null, CanRetry: false);
		}

		return new StateInfo(MokatermIcons.FolderOpen, "No folder open", "The start folder could not be opened.", CanRetry: true);
	}

	private sealed record PendingNavigation(string Folder, string? Select, bool ReportFailure);

	private sealed record StateInfo(MokaIconDefinition Icon, string Title, string? Description, bool CanRetry);
}
