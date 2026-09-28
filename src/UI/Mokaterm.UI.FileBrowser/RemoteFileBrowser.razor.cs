using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moka.Red.ContextMenu;
using Moka.Red.Core.Icons;
using Moka.Red.Data.Table;
using Moka.Red.Feedback.Dialog;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
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
/// Re-opens the file system when the session reconnects.
/// </summary>
public partial class RemoteFileBrowser : ComponentBase, IAsyncDisposable
{
	// Beyond this many rows the table renders only the rows on screen.
	private const int VirtualizeThreshold = 300;

	// A dense row: the 16px line of the cell content, 4px cell padding above and below, and the 1px border.
	private const float RowHeight = 25;

	// Widths in CSS pixels where the browser reaches BrowserWidth.Medium and BrowserWidth.Wide. Medium fits the name,
	// size and modified columns, Wide every column, each at its set width.
	private static readonly int[] WidthSteps = [380, 620];

	private const string UserMode = "user";
	private const string RootMode = "root";
	private const string ToolbarStyle = "border-bottom:1px solid var(--moka-color-outline-variant)";

	private static readonly string[] InitialDirectoryOptions = ["ssh.initialDirectory", "ftp.initialDirectory"];

	private static readonly Func<RemoteFileEntry, object> EntryKey = entry => entry.Path;
	private static readonly Func<RemoteFileEntry, object?> NameField = entry => entry.Name;
	private static readonly Func<RemoteFileEntry, object?> SizeField = entry => entry.Size;
	private static readonly Func<RemoteFileEntry, object?> ModifiedField = entry => entry.LastModified;
	private static readonly Func<RemoteFileEntry, object?> PermissionsField = entry => entry.Permissions;
	private static readonly Func<RemoteFileEntry, object?> OwnerField = EntryOrdering.OwnerLabel;

	private readonly CancellationTokenSource _lifetime = new();
	private readonly EntrySelection _selection = new();
	private readonly NavigationHistory _history = new();

	private ElementReference _root;
	private DotNetObjectReference<FileBrowserJsReceiver>? _receiver;
	private IJSObjectReference? _jsHandle;
	private IAsyncDisposable? _dropRegistration;
	private FileBrowserDialogs? _dialogs;
	private bool _activeSentToJs;
	private bool _disposed;

	private ISessionHandle? _session;
	private IProtocolSession? _protocolSession;
	private IFileSystemFeature? _feature;
	private IRemoteFileSystem? _userFileSystem;
	private IRemoteFileSystem? _rootFileSystem;
	private IRemoteFileSystem? _fileSystem;
	private CancellationTokenSource? _connection;
	private CancellationTokenSource? _load;
	private bool _opening;
	private string? _openError;

	private string _path = RemotePath.Root;
	private bool _loaded;
	private bool _loading;
	private IReadOnlyList<RemoteFileEntry> _entries = [];
	private DirectoryView _view = DirectoryView.Empty;
	private HashSet<RemoteFileEntry> _selectedItems = [];
	private string? _sortColumn = EntryOrdering.NameColumn;
	private MokaSortDirection _sortDirection = MokaSortDirection.Ascending;
	private bool _foldersFirst = true;
	private string _filter = "";

	private BrowserAlert? _alert;
	private int _busy;
	private bool _switchingMode;
	private bool _dragOver;
	private bool _editingPath;
	private BrowserWidth _width = BrowserWidth.Narrow;
	private string _pathText = "";

	// Work for the next OnAfterRenderAsync, once the DOM matches the new state.
	private int? _scrollToIndex;
	private bool _restoreFocus;
	private string? _focusSelector;

	[Parameter, EditorRequired]
	public ISessionHandle Session { get; set; } = default!;

	/// <summary>True while visible. Hidden browsers stop polling and ignore keyboard shortcuts.</summary>
	[Parameter]
	public bool IsActive { get; set; } = true;

	/// <summary>Starting directory. Null starts in the user's home directory.</summary>
	[Parameter]
	public string? InitialPath { get; set; }

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
	private ILogger<RemoteFileBrowser> Logger { get; set; } = default!;

	private FileBrowserDialogs Dialogs => _dialogs ??= new FileBrowserDialogs(DialogService);

	private CancellationToken ConnectionToken =>
		_disposed ? new CancellationToken(canceled: true) : _connection?.Token ?? _lifetime.Token;

	private IProtocolSession? LiveSession => Session.State == SessionState.Connected ? Session.Session : null;

	/// <summary>A file system is open and a listing is on screen.</summary>
	private bool CanBrowse => _fileSystem is not null && _loaded;

	private bool IsElevated => _fileSystem?.IsElevated == true;

	private bool CanElevate => _feature?.SupportsElevation == true;

	private bool IsBusy => _loading || _opening || _switchingMode || _busy > 0;

	private bool ShowHidden => Settings.Get<FileTransferSettings>().ShowHiddenFiles;

	// While root is being opened (and the sudo prompt may be up) the control already shows the requested mode.
	private string ModeValue => IsElevated || _switchingMode ? RootMode : UserMode;

	private TransferOrigin Origin => new(Session.Title, Session.Host.Address);

	private List<RemoteFileEntry> SelectedEntries => _selection.SelectedIn(_view);

	private string RootClass => $"fb{(IsElevated ? " fb--root" : "")} {Class}".TrimEnd();

	private string AccountLabel => _fileSystem is null ? "" : _fileSystem.UserName + "@" + Session.Host.Address;

	private string DropAccountText => "as " + _fileSystem?.UserName;

	private string NoMatchesText => "Nothing here matches \"" + _filter.Trim() + "\".";

	private string HiddenOnlyText => _entries.Count == 1
		? "This folder holds 1 hidden item."
		: string.Create(CultureInfo.CurrentCulture, $"This folder holds {_entries.Count:N0} hidden items.");

	private bool IsNarrow => _width == BrowserWidth.Narrow;

	private string ItemCountText
	{
		get
		{
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
		if (_session is not null)
		{
			_session.Changed -= OnSessionChanged;
		}

		Settings.Changed -= OnSettingsChanged;
		_load?.Cancel();
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
		_foldersFirst = Settings.Get<FileTransferSettings>().FoldersFirst;
	}

	protected override void OnParametersSet()
	{
		if (ReferenceEquals(Session, _session))
		{
			return;
		}

		if (_session is not null)
		{
			_session.Changed -= OnSessionChanged;
		}

		_session = Session;
		_session.Changed += OnSessionChanged;
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

		if (_scrollToIndex is { } index)
		{
			_scrollToIndex = null;
			await _jsHandle.TryInvokeVoidAsync("scrollToRow", index);
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
		_protocolSession = null;
		_feature = null;
		_userFileSystem = null;
		_rootFileSystem = null;
		_fileSystem = null;
		_opening = false;
		_openError = null;
		_path = RemotePath.Root;
		_loaded = false;
		_loading = false;
		_entries = [];
		_filter = "";
		_editingPath = false;
		_dragOver = false;
		_history.Clear();
		_selection.Reset();
		RebuildView();
		CloseAlert();
	}

	private void CancelConnection()
	{
		_load?.Cancel();
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

		IProtocolSession? live = LiveSession;
		if (ReferenceEquals(live, _protocolSession))
		{
			StateHasChanged();
			return;
		}

		// The file systems of the old connection belong to the session, which disposes them; only forget them here.
		CancelConnection();
		_protocolSession = live;
		_feature = live?.GetFeature<IFileSystemFeature>();
		_userFileSystem = null;
		_rootFileSystem = null;
		_fileSystem = null;
		_opening = false;
		_openError = null;
		_loading = false;
		_dragOver = false;
		_editingPath = false;
		StateHasChanged();

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

		// A reconnect keeps the folder the user was in; the first connection starts where the settings say.
		if (_loaded)
		{
			await RefreshAsync();
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
				LoadOutcome outcome = await LoadAsync(fileSystem, startPath, select: null);
				if (outcome.Error is not { } startError)
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

		LoadOutcome homeOutcome = await LoadAsync(fileSystem, home, select: null);
		if (homeOutcome.Error is { } error)
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

			foreach (string key in InitialDirectoryOptions)
			{
				if (Session.Connection.Options.GetString(key) is { } value && !string.IsNullOrWhiteSpace(value))
				{
					return value.Trim();
				}
			}

			return null;
		}
	}

	/// <summary>
	/// Lists <paramref name="path"/> and shows it. A newer load or a new connection supersedes this one, which then
	/// changes nothing. Returns the failure for the caller to report.
	/// </summary>
	private async Task<LoadOutcome> LoadAsync(IRemoteFileSystem fileSystem, string path, string? select)
	{
		_load?.Cancel();
		using CancellationTokenSource load = CancellationTokenSource.CreateLinkedTokenSource(ConnectionToken);
		_load = load;
		_loading = true;
		StateHasChanged();
		try
		{
			IReadOnlyList<RemoteFileEntry> entries = await fileSystem.ListAsync(path, load.Token);
			if (load.IsCancellationRequested || !ReferenceEquals(fileSystem, _fileSystem))
			{
				return LoadOutcome.Superseded;
			}

			if (!_loaded || _path != path)
			{
				_selection.Reset();
				_filter = "";
			}

			_path = path;
			_entries = entries;
			_loaded = true;
			RebuildView();
			if (select is not null)
			{
				Reveal(select);
			}

			return LoadOutcome.Loaded;
		}
		catch (Exception ex)
		{
			return load.IsCancellationRequested || !ReferenceEquals(fileSystem, _fileSystem)
				? LoadOutcome.Superseded
				: LoadOutcome.Failed(ex);
		}
		finally
		{
			if (ReferenceEquals(_load, load))
			{
				_load = null;
				_loading = false;
			}

			StateHasChanged();
		}
	}

	private async Task NavigateAsync(string path, NavigationKind kind = NavigationKind.Push, string? select = null)
	{
		if (_fileSystem is not { } fileSystem)
		{
			return;
		}

		string target = RemotePath.Normalize(path);
		string previous = _path;
		bool hadListing = _loaded;
		_editingPath = false;

		LoadOutcome outcome = await LoadAsync(fileSystem, target, select);
		if (outcome.Error is { } error)
		{
			ShowAlert("Could not open " + target, error, _ => NavigateAsync(target, kind, select));
			return;
		}

		if (!outcome.IsLoaded)
		{
			return;
		}

		if (hadListing && previous != target)
		{
			switch (kind)
			{
				case NavigationKind.Back:
					_history.CompleteBack(previous);
					break;
				case NavigationKind.Forward:
					_history.CompleteForward(previous);
					break;
				default:
					_history.Push(previous);
					break;
			}
		}

		CloseAlert();
		_restoreFocus = true;
	}

	private Task GoBackAsync() =>
		_history.PeekBack() is { } path ? NavigateAsync(path, NavigationKind.Back, select: _path) : Task.CompletedTask;

	private Task GoForwardAsync() =>
		_history.PeekForward() is { } path ? NavigateAsync(path, NavigationKind.Forward, select: _path) : Task.CompletedTask;

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
		if (_fileSystem is not { } fileSystem)
		{
			return;
		}

		if (!_loaded)
		{
			await LoadStartAsync();
			return;
		}

		string path = _path;
		LoadOutcome outcome = await LoadAsync(fileSystem, path, select);
		if (outcome.Error is { } error)
		{
			ShowAlert("Could not refresh " + path, error, _ => RefreshAsync(select));
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

	private void RebuildView()
	{
		FileTransferSettings settings = Settings.Get<FileTransferSettings>();
		_foldersFirst = settings.FoldersFirst;
		bool sorted = _sortColumn is not null && _sortDirection != MokaSortDirection.None;
		_view = DirectoryView.Build(
			_entries,
			settings.ShowHiddenFiles,
			_filter,
			sorted ? _sortColumn : null,
			_sortDirection == MokaSortDirection.Descending,
			_foldersFirst);
		_selection.Retain(_view);
		UpdateSelectedItems();
	}

	// The table ignores changes made to the set it was given, so every change hands it a new one.
	private void UpdateSelectedItems() => _selectedItems = [.. _selection.SelectedIn(_view)];

	/// <summary>Selects <paramref name="path"/> when it is visible and scrolls to it after the next render.</summary>
	private void Reveal(string path)
	{
		int index = _view.IndexOf(path);
		if (index >= 0)
		{
			_selection.SelectOnly(path);
			UpdateSelectedItems();
			_scrollToIndex = index;
		}
	}

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

	// The table sorts with the column's comparer; the browser's view uses the same order, so row indexes line up.
	private void OnSortColumnChanged(string? column)
	{
		_sortColumn = column;
		RebuildView();
	}

	private void OnSortDirectionChanged(MokaSortDirection direction)
	{
		_sortDirection = direction;
		RebuildView();
	}

	private int CompareByName(RemoteFileEntry a, RemoteFileEntry b) => CompareBy(EntryOrdering.NameColumn, a, b);

	private int CompareBySize(RemoteFileEntry a, RemoteFileEntry b) => CompareBy(EntryOrdering.SizeColumn, a, b);

	private int CompareByModified(RemoteFileEntry a, RemoteFileEntry b) => CompareBy(EntryOrdering.ModifiedColumn, a, b);

	private int CompareByPermissions(RemoteFileEntry a, RemoteFileEntry b) => CompareBy(EntryOrdering.PermissionsColumn, a, b);

	private int CompareByOwner(RemoteFileEntry a, RemoteFileEntry b) => CompareBy(EntryOrdering.OwnerColumn, a, b);

	private int CompareBy(string column, RemoteFileEntry a, RemoteFileEntry b) =>
		EntryOrdering.Compare(a, b, column, _sortDirection == MokaSortDirection.Descending, _foldersFirst);

	private bool HasFeature(RemoteFileSystemFeatures feature) => _fileSystem is not null && (_fileSystem.Features & feature) == feature;

	private StateInfo DescribeState()
	{
		if (Session.State == SessionState.Connecting)
		{
			return new StateInfo(MokatermIcons.Connect, "Connecting...", Session.StatusMessage, CanRetry: false);
		}

		// The states a reconnect acts on are the states with no file system behind them.
		if (ISessionHandle.CanReconnect(Session.State))
		{
			return new StateInfo(MokatermIcons.Disconnect, "Disconnected", Session.StatusMessage, CanRetry: false);
		}

		if (_protocolSession is not null && _feature is null)
		{
			return new StateInfo(MokatermIcons.Folder, "No file access", "This connection does not offer a file system.", CanRetry: false);
		}

		if (_openError is not null)
		{
			return new StateInfo(MokaIcons.Status.Warning, "Could not open files", _openError, CanRetry: true);
		}

		if (_fileSystem is null || _opening || _loading)
		{
			return new StateInfo(MokatermIcons.FolderOpen, "Opening files...", null, CanRetry: false);
		}

		return new StateInfo(MokatermIcons.FolderOpen, "No folder open", "The start folder could not be opened.", CanRetry: true);
	}

	private enum NavigationKind
	{
		Push,
		Back,
		Forward,
	}

	private readonly record struct LoadOutcome(bool IsLoaded, Exception? Error)
	{
		public static LoadOutcome Loaded => new(true, null);

		public static LoadOutcome Superseded => new(false, null);

		public static LoadOutcome Failed(Exception error) => new(false, error);
	}

	private sealed record StateInfo(MokaIconDefinition Icon, string Title, string? Description, bool CanRetry);
}
