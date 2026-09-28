using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moka.Red.ContextMenu;
using Moka.Red.Core.Enums;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Interaction;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Terminal.Internal;
using Mokaterm.UI.Terminal.Interop;

namespace Mokaterm.UI.Terminal;

/// <summary>
/// An xterm.js terminal bound to an <see cref="ITerminalStream"/>. Attaches on first render and detaches on
/// dispose; the stream keeps running, so the view can be remounted without losing the session.
/// </summary>
public partial class TerminalView : ComponentBase, IAsyncDisposable
{
	private readonly TaskCompletionSource<int> _instance = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly CancellationTokenSource _disposeCts = new();
	private ElementReference _host;
	private ElementReference _screen;
	private DotNetObjectReference<TerminalJsCallbacks>? _callbacks;
	private int _instanceId;
	private ITerminalStream? _attachedStream;
	private IDisposable? _attachment;
	private TerminalOutputSink? _sink;
	private TerminalJsOptions? _options;
	private TerminalJsOptions? _appliedOptions;
	private TerminalSize? _gridSize;
	private string? _hostStyle;
	private string? _startError;
	private bool _wasActive;
	private bool _activatePending;
	private bool _focusPending;
	private bool _switchingStream;
	private bool _disposed;

	private bool _scrolledUp;

	private string? _hoverCommand;
	private string? _hoverStyle;

	private bool _findOpen;
	private bool _findFocusPending;
	private bool _findCaseSensitive;
	private bool _findWholeWord;
	private bool _findRegex;
	private string _findTerm = "";
	private string? _findStatus;
	private bool _searching;
	private (bool Forward, bool Incremental)? _queuedSearch;

	[Parameter, EditorRequired]
	public ITerminalStream Stream { get; set; } = default!;

	/// <summary>Effective settings: the global terminal settings with the connection's overrides applied.</summary>
	[Parameter, EditorRequired]
	public TerminalSettings Settings { get; set; } = new();

	/// <summary>
	/// The color of the machine on the other end, for the host in a prompt the server leaves plain. A CSS color or a
	/// <c>var(...)</c>; null falls back to the theme's cyan.
	/// </summary>
	[Parameter]
	public string? AccentColor { get; set; }

	/// <summary>True while the view is visible. Turning true re-fits the terminal to its container and focuses it.</summary>
	[Parameter]
	public bool IsActive { get; set; } = true;

	/// <summary>Raised when the remote side sets the window title (OSC 0/2).</summary>
	[Parameter]
	public EventCallback<string> OnTitleChanged { get; set; }

	/// <summary>Raised after the grid size changed and was sent to the stream.</summary>
	[Parameter]
	public EventCallback<TerminalSize> OnResized { get; set; }

	/// <summary>
	/// True while the stream is closed and can be reopened. The view then offers Enter to reconnect and raises
	/// <see cref="OnReconnectRequested"/> for it instead of dropping the key.
	/// </summary>
	[Parameter]
	public bool CanReconnect { get; set; }

	[Parameter]
	public EventCallback OnReconnectRequested { get; set; }

	/// <summary>
	/// Raised when the user turned the multi-line paste question off from the prompt itself. The parent owns the
	/// setting, so it is the one that writes it.
	/// </summary>
	[Parameter]
	public EventCallback OnStopConfirmingPaste { get; set; }

	/// <summary>Appended below the terminal's own context menu items (copy, paste, select all, find, clear).</summary>
	[Parameter]
	public IReadOnlyList<MokaContextMenuItem>? ExtraMenuItems { get; set; }

	/// <summary>
	/// Shows two buttons on the hovered row when it holds a command: save it as it is, or save it with a name, tags
	/// and a scope. Needs <see cref="OnCommandSaved"/> and <see cref="OnCommandSavedWithDetails"/>.
	/// </summary>
	[Parameter]
	public bool ShowCommandActions { get; set; }

	/// <summary>The hovered row's command, saved without asking anything.</summary>
	[Parameter]
	public EventCallback<string> OnCommandSaved { get; set; }

	/// <summary>The hovered row's command, to be named, tagged and scoped before it is saved.</summary>
	[Parameter]
	public EventCallback<string> OnCommandSavedWithDetails { get; set; }

	[Parameter]
	public string? Class { get; set; }

	[Parameter]
	public string? Style { get; set; }

	[Inject]
	private TerminalInterop Interop { get; set; } = default!;

	[Inject]
	private ITerminalThemeCatalog ThemeCatalog { get; set; } = default!;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private IMokaContextMenuService ContextMenu { get; set; } = default!;

	[Inject]
	private ILogger<TerminalView> Logger { get; set; } = default!;

	private bool IsReady => !_disposed && _instanceId != 0;

	private string HostClass => string.IsNullOrWhiteSpace(Class) ? "mt-terminal" : "mt-terminal " + Class;

	private bool ShowReconnectHint => CanReconnect && OnReconnectRequested.HasDelegate && _startError is null;

	private bool CommandActionsEnabled =>
		ShowCommandActions && _startError is null && (OnCommandSaved.HasDelegate || OnCommandSavedWithDetails.HasDelegate);

	private TerminalSearchFlags SearchFlags => new(_findCaseSensitive, _findWholeWord, _findRegex);

	public ValueTask FocusAsync()
	{
		if (_instanceId == 0)
		{
			// Before the xterm.js instance exists; focus once it does.
			_focusPending = !_disposed;
			return ValueTask.CompletedTask;
		}

		return RunAsync(id => Interop.FocusAsync(id));
	}

	/// <summary>Clears the scrollback and screen of the view (not of the remote application).</summary>
	public ValueTask ClearAsync() => RunAsync(id => Interop.ClearAsync(id));

	public ValueTask SelectAllAsync() => RunAsync(id => Interop.SelectAllAsync(id));

	public async ValueTask CopySelectionAsync()
	{
		if (!IsReady)
		{
			return;
		}

		try
		{
			string text = await Interop.GetSelectionAsync(_instanceId);
			if (text.Length > 0)
			{
				await Clipboard.WriteTextAsync(text, _disposeCts.Token);
			}
		}
		catch (JSException ex)
		{
			Logger.LogDebug(ex, "Copying the terminal selection failed.");
			Interaction.Notify(NoticeSeverity.Warning, "The clipboard cannot be written here.", "Copy failed");
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The view is closing.
		}
	}

	/// <summary>Pastes clipboard text, asking first when it spans lines and the settings say so.</summary>
	public async ValueTask PasteAsync()
	{
		if (!IsReady)
		{
			return;
		}

		try
		{
			// terminal.js reads the clipboard and runs the multi-line confirmation, shared with pastes that never reach .NET.
			await Interop.PasteFromClipboardAsync(_instanceId);
		}
		catch (JSException ex)
		{
			Logger.LogDebug(ex, "Pasting into the terminal failed.");
			Interaction.Notify(NoticeSeverity.Warning, "The clipboard cannot be read here.", "Paste failed");
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The view is closing.
		}
	}

	/// <summary>Opens the find bar.</summary>
	public ValueTask OpenFindAsync()
	{
		if (_disposed)
		{
			return ValueTask.CompletedTask;
		}

		return new ValueTask(InvokeAsync(() =>
		{
			_findOpen = true;
			_findFocusPending = true;
			StateHasChanged();
		}));
	}

	/// <summary>Re-measures the container and resizes the grid.</summary>
	public ValueTask FitAsync() => RunAsync(id => Interop.FitAsync(id));

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		GC.SuppressFinalize(this);
		await _disposeCts.CancelAsync();
		Detach();
		_instance.TrySetCanceled();
		if (_instanceId != 0)
		{
			try
			{
				await Interop.TryDisposeInstanceAsync(_instanceId);
			}
			catch (JSException ex)
			{
				Logger.LogDebug(ex, "Disposing the xterm.js instance failed.");
			}

			_instanceId = 0;
		}

		_callbacks?.Dispose();
		_disposeCts.Dispose();
	}

	protected override void OnParametersSet()
	{
		TerminalTheme theme = ThemeCatalog.Get(Settings.ThemeId);
		_options = TerminalJsOptions.From(Settings, theme, AccentColor);

		// Painting the theme background from the first render avoids a flash of the page color before xterm.js draws.
		_hostStyle = CssValues.Color(theme.Background) is { } background ? $"background-color:{background};{Style}" : Style;

		if (IsActive && !_wasActive)
		{
			_activatePending = true;
		}

		_wasActive = IsActive;
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (_disposed)
		{
			return;
		}

		try
		{
			if (firstRender && !await TryCreateTerminalAsync())
			{
				return;
			}

			if (IsReady)
			{
				await ApplyPendingChangesAsync();
			}
		}
		catch (JSException ex)
		{
			Logger.LogError(ex, "The terminal view failed in JavaScript.");
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The circuit or WebView closed while the terminal was updating.
		}
	}

	internal Task HandleInputAsync(string data)
	{
		// The stream drops input while closed, so Enter there has nothing better to do than reconnect.
		if (ShowReconnectHint && data.Contains('\r', StringComparison.Ordinal))
		{
			return RequestReconnectAsync();
		}

		return SendInputAsync((stream, cancellationToken) => stream.SendTextAsync(data, cancellationToken));
	}

	internal Task HandleBinaryInputAsync(byte[] data) =>
		SendInputAsync((stream, cancellationToken) => stream.SendAsync(data, cancellationToken));

	internal async Task HandleResizeAsync(TerminalSize size)
	{
		if (_disposed || !size.IsValid)
		{
			return;
		}

		_gridSize = size;
		await ResizeStreamAsync(_attachedStream ?? Stream, size);
		if (!_disposed)
		{
			await OnResized.InvokeAsync(size);
		}
	}

	internal Task HandleTitleChangedAsync(string title) =>
		_disposed ? Task.CompletedTask : OnTitleChanged.InvokeAsync(title);

	internal void ShowContextMenu(double x, double y, bool hasSelection)
	{
		if (!IsReady)
		{
			return;
		}

		List<MokaContextMenuItem> items =
		[
			new() { Text = "Copy", Icon = MokaIcons.Content.Copy, Shortcut = "Ctrl+Shift+C", Disabled = !hasSelection, OnClick = () => ThenFocusAsync(CopySelectionAsync) },
			new() { Text = "Paste", Icon = MokaIcons.Content.Paste, Shortcut = "Ctrl+Shift+V", OnClick = () => ThenFocusAsync(PasteAsync) },
			new() { Text = "Select all", Icon = MokatermIcons.Maximize, OnClick = () => ThenFocusAsync(SelectAllAsync) },
			new() { Text = "Find", Icon = MokaIcons.Action.Search, Shortcut = "Ctrl+Shift+F", OnClick = () => OpenFindAsync().AsTask() },
			new() { Text = "Clear", Icon = MokatermIcons.Eraser, OnClick = () => ThenFocusAsync(ClearAsync) },
		];

		if (ExtraMenuItems is { Count: > 0 } extra)
		{
			items.Add(MokaContextMenuItems.Divider());
			items.AddRange(extra);
		}

		ContextMenu.Show(x, y, items);
	}

	internal async Task<bool> ConfirmPasteAsync(string preview, int lineCount)
	{
		if (_disposed)
		{
			return false;
		}

		string summary = lineCount <= 1
			? "The text ends with a line break, so it runs as soon as it is pasted."
			: string.Create(CultureInfo.CurrentCulture, $"The text has {lineCount} lines, and each line break can run a command.");
		ConfirmPrompt prompt = new()
		{
			Title = "Paste with line breaks?",
			Message = summary,
			Details = preview,
			ConfirmText = "Paste",
			RememberText = OnStopConfirmingPaste.HasDelegate ? PromptOptOut.Label : null,
		};

		try
		{
			ConfirmResult answer = await Interaction.ConfirmAsync(prompt, _disposeCts.Token);
			if (answer.Remember)
			{
				await OnStopConfirmingPaste.InvokeAsync();
			}

			return answer.Confirmed;
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
		{
			return false;
		}
	}

	internal void HandleScrollStateChanged(bool atBottom)
	{
		if (_disposed || _scrolledUp != atBottom)
		{
			return;
		}

		_scrolledUp = !atBottom;
		StateHasChanged();
	}

	/// <summary>
	/// The row under the pointer, from terminal.js. Rows that are output rather than a command leave the buttons off,
	/// which is also how an empty row clears them.
	/// </summary>
	internal void HandleRowHover(string text, int top, int height)
	{
		if (_disposed)
		{
			return;
		}

		string? command = CommandActionsEnabled && TerminalPrompt.TryExtractCommand(text, out string extracted) ? extracted : null;
		string? style = command is null
			? null
			: string.Create(CultureInfo.InvariantCulture, $"top:{top}px;height:{height}px");

		if (command == _hoverCommand && style == _hoverStyle)
		{
			return;
		}

		_hoverCommand = command;
		_hoverStyle = style;
		StateHasChanged();
	}

	internal void HandleSearchResults(int index, int count)
	{
		if (_disposed || !_findOpen || _findTerm.Length == 0)
		{
			return;
		}

		_findStatus = FormatFindStatus(new TerminalSearchResult(count > 0, index, count));
		StateHasChanged();
	}

	// A failed script import stays failed for the life of the page, so there is nothing to retry; the view says why it is
	// empty instead of staying blank.
	private async Task<bool> TryCreateTerminalAsync()
	{
		try
		{
			await CreateTerminalAsync();
			return true;
		}
		catch (JSException ex)
		{
			Logger.LogError(ex, "The terminal view could not be created.");
			_startError = ex.Message.Split('\n', 2)[0].Trim();
			StateHasChanged();
			return false;
		}
	}

	private async Task CreateTerminalAsync()
	{
		TerminalJsOptions options = _options ?? TerminalJsOptions.From(Settings, ThemeCatalog.Get(Settings.ThemeId), AccentColor);
		ITerminalStream stream = Stream;
		_callbacks = DotNetObjectReference.Create(new TerminalJsCallbacks(this));
		TerminalCreateResult created = await Interop.CreateAsync(_host, _screen, _callbacks, options, stream.Size);
		if (_disposed)
		{
			await Interop.TryDisposeInstanceAsync(created.Id);
			return;
		}

		_instanceId = created.Id;
		_appliedOptions = options;
		_instance.TrySetResult(created.Id);

		// Size the remote side before its output starts arriving, so the replay and live output match the grid.
		if (created.Size is { } size)
		{
			await HandleResizeAsync(new TerminalSize(size.Cols, size.Rows, size.Width, size.Height));
		}

		if (!_disposed)
		{
			Attach(stream);
		}
	}

	private async Task ApplyPendingChangesAsync()
	{
		int id = _instanceId;

		// Blazor does not wait for OnAfterRenderAsync before the next render, so a second pass can arrive while a
		// switch is awaiting; the flag keeps it from attaching twice, and the loop picks up a stream that changed again.
		if (!_switchingStream)
		{
			_switchingStream = true;
			try
			{
				while (!_disposed && !ReferenceEquals(Stream, _attachedStream))
				{
					ITerminalStream stream = Stream;
					Detach();
					await Interop.ResetAsync(id);
					if (_gridSize is { } grid)
					{
						await ResizeStreamAsync(stream, grid);
					}

					if (!_disposed && ReferenceEquals(stream, Stream))
					{
						Attach(stream);
					}
				}
			}
			finally
			{
				_switchingStream = false;
			}
		}

		if (_options is { } options && _appliedOptions is { } applied && !ReferenceEquals(options, applied))
		{
			_appliedOptions = options;
			Dictionary<string, object?> changes = options.ChangesSince(applied);
			if (changes.Count > 0)
			{
				await Interop.SetOptionsAsync(id, changes);
			}
		}

		if (_activatePending)
		{
			_activatePending = false;
			if (IsActive)
			{
				// The view was display:none while inactive, so its grid may be stale.
				await Interop.FitAsync(id);
				_focusPending = true;
			}
		}

		if (_findFocusPending)
		{
			_findFocusPending = false;
			_focusPending = false;
			await Interop.FocusFindInputAsync(_host);
		}
		else if (_focusPending)
		{
			_focusPending = false;
			await Interop.FocusAsync(id);
		}
	}

	private void Attach(ITerminalStream stream)
	{
		_sink = new TerminalOutputSink(Interop, _instance.Task, work => InvokeAsync(work));
		_attachment = stream.Attach(_sink);
		_attachedStream = stream;
	}

	private void Detach()
	{
		// The sink goes first: it releases a write still waiting on xterm.js, so detaching cannot wait on the view.
		_sink?.Dispose();
		_attachment?.Dispose();
		_sink = null;
		_attachment = null;
		_attachedStream = null;
	}

	private async Task ResizeStreamAsync(ITerminalStream stream, TerminalSize size)
	{
		if (stream.Size.Columns == size.Columns && stream.Size.Rows == size.Rows)
		{
			return;
		}

		try
		{
			await stream.ResizeAsync(size, _disposeCts.Token);
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
		{
			// The view closed during the resize.
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "Resizing the remote terminal failed.");
		}
	}

	// Input is never logged: it can hold passwords typed at a prompt.
	private async Task SendInputAsync(Func<ITerminalStream, CancellationToken, ValueTask> send)
	{
		if (_disposed || _attachedStream is not { } stream)
		{
			return;
		}

		try
		{
			await send(stream, _disposeCts.Token);
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
		{
			// The view closed while the input was on its way.
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "Sending terminal input failed.");
		}
	}

	private async ValueTask RunAsync(Func<int, ValueTask> call)
	{
		if (!IsReady)
		{
			return;
		}

		try
		{
			await call(_instanceId);
		}
		catch (JSException ex)
		{
			// Menu actions run inside event handlers, where an unhandled exception would end a Blazor Server circuit.
			Logger.LogDebug(ex, "A terminal command failed in JavaScript.");
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The view is closing.
		}
	}

	private static MokaVariant OptionVariant(bool on) => on ? MokaVariant.Soft : MokaVariant.Text;

	private static MokaColor OptionColor(bool on) => on ? MokaColor.Primary : MokaColor.Surface;

	private static string AriaPressed(bool on) => on ? "true" : "false";

	private async Task RequestReconnectAsync()
	{
		if (!_disposed && ShowReconnectHint)
		{
			await OnReconnectRequested.InvokeAsync();
		}
	}

	private Task ScrollToBottomAsync() => ThenFocusAsync(() => RunAsync(id => Interop.ScrollToBottomAsync(id)));

	private async Task SaveHoveredCommandAsync(bool withDetails)
	{
		if (_hoverCommand is not { } command)
		{
			return;
		}

		EventCallback<string> callback = withDetails ? OnCommandSavedWithDetails : OnCommandSaved;
		if (callback.HasDelegate)
		{
			await callback.InvokeAsync(command);
		}
	}

	private Task ToggleCaseSensitiveAsync()
	{
		_findCaseSensitive = !_findCaseSensitive;
		return SearchAgainAsync();
	}

	private Task ToggleWholeWordAsync()
	{
		_findWholeWord = !_findWholeWord;
		return SearchAgainAsync();
	}

	private Task ToggleRegexAsync()
	{
		_findRegex = !_findRegex;
		return SearchAgainAsync();
	}

	// Changed options apply to the match already found, so the search starts from it rather than moving on.
	private Task SearchAgainAsync() => SearchAsync(forward: true, incremental: true);

	private async Task ThenFocusAsync(Func<ValueTask> action)
	{
		await action();
		await FocusAsync();
	}

	private async Task OnFindTermChangedAsync(string value)
	{
		_findTerm = value;
		await SearchAsync(forward: true, incremental: true);
	}

	private async Task OnFindKeyDownAsync(KeyboardEventArgs args)
	{
		if (args.AltKey && !args.CtrlKey && !args.MetaKey)
		{
			switch (args.Code)
			{
				case "KeyC":
					await ToggleCaseSensitiveAsync();
					return;
				case "KeyW":
					await ToggleWholeWordAsync();
					return;
				case "KeyR":
					await ToggleRegexAsync();
					return;
			}
		}

		switch (args.Key)
		{
			case "Enter":
				await SearchAsync(forward: !args.ShiftKey, incremental: false);
				break;
			case "Escape":
				await CloseFindAsync();
				break;
		}
	}

	private Task FindNextAsync() => SearchAsync(forward: true, incremental: false);

	private Task FindPreviousAsync() => SearchAsync(forward: false, incremental: false);

	// Typing searches on every keystroke; while a search runs, only the newest request is kept.
	private async Task SearchAsync(bool forward, bool incremental)
	{
		if (!IsReady)
		{
			return;
		}

		if (_searching)
		{
			_queuedSearch = (forward, incremental);
			return;
		}

		_searching = true;
		try
		{
			(bool Forward, bool Incremental)? next = (forward, incremental);
			while (next is { } request && IsReady)
			{
				_queuedSearch = null;
				TerminalSearchResult? result = null;
				if (_findTerm.Length == 0)
				{
					await Interop.ClearSearchAsync(_instanceId);
				}
				else
				{
					result = request.Forward
						? await Interop.FindNextAsync(_instanceId, _findTerm, request.Incremental, SearchFlags)
						: await Interop.FindPreviousAsync(_instanceId, _findTerm, SearchFlags);
				}

				_findStatus = FormatFindStatus(result);
				next = _queuedSearch;
			}
		}
		catch (JSException ex)
		{
			Logger.LogDebug(ex, "Searching the terminal failed.");
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The view is closing.
		}
		finally
		{
			_searching = false;
		}
	}

	private async Task CloseFindAsync()
	{
		_findOpen = false;
		_findStatus = null;
		await RunAsync(async id =>
		{
			await Interop.ClearSearchAsync(id);
			await Interop.FocusAsync(id);
		});
	}

	private static string? FormatFindStatus(TerminalSearchResult? result) => result switch
	{
		null => null,
		{ Invalid: true } => "Invalid pattern",
		{ Count: 0 } => "No results",
		{ Index: >= 0 } active => string.Create(CultureInfo.CurrentCulture, $"{active.Index + 1}/{active.Count}"),
		// The search addon stops counting at its highlight limit and reports no active index past it.
		TerminalSearchResult limited => string.Create(CultureInfo.CurrentCulture, $"{limited.Count}+"),
	};
}
