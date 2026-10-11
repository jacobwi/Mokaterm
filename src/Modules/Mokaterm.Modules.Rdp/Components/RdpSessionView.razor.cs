using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moka.Red.ContextMenu;
using Moka.Red.Core.Enums;
using Moka.Red.Core.Icons;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Rdp.Interop;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.Modules.Rdp.Components;

/// <summary>
/// The remote desktop of an RDP session: a thin toolbar over a canvas the session paints. The canvas is created on
/// the first render and destroyed when the view unmounts; the session keeps the connection and the desktop, so a
/// view that comes back is repainted without the user doing anything.
/// </summary>
public sealed partial class RdpSessionView : SessionViewBase, IAsyncDisposable
{
	// A full screen PNG of a large desktop; well above what a 4K screen produces.
	private const long MaxScreenshotBytes = 64 * 1024 * 1024;

	private const string ScalingFit = "fit";
	private const string ScalingActual = "actual";
	private const string ScalingRemote = "remote";

	private readonly CancellationTokenSource _disposeCts = new();
	private ElementReference _root;
	private ElementReference _screen;
	private DotNetObjectReference<RdpJsCallbacks>? _callbacks;
	private ISessionHandle? _handle;
	private IProtocolSession? _attachedSession;
	private IRdpChannel? _channel;
	private RdpPageSink? _sink;
	private RdpConnectionInfo? _info;
	private ScreenState _state = ScreenState.Idle;
	private string? _error;
	private string _scaling = ScalingFit;
	private bool _viewOnly;
	private int _instanceId;
	private int _screenWidth;
	private int _screenHeight;
	private bool _fullscreen;
	private bool _pendingAttach;
	private bool _pendingActive;
	private bool _activeApplied;
	private bool _disposed;

	private enum ScreenState
	{
		Idle,
		Starting,
		Running,
		Ended,
		Failed,
	}

	[Inject]
	private RdpInterop Interop { get; set; } = default!;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private ILocalFileAccess LocalFiles { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private IMokaContextMenuService ContextMenu { get; set; } = default!;

	[Inject]
	private ILogger<RdpSessionView> Logger { get; set; } = default!;

	private bool IsRunning => _state == ScreenState.Running && _instanceId != 0;

	private string IdleDescription => SessionIdleText.For(Session.State, "Reconnect the session to open its desktop again.");

	private string ScreenSizeText => _screenWidth > 0
		? string.Create(CultureInfo.InvariantCulture, $"{_screenWidth} x {_screenHeight}")
		: "";

	private string SecurityText => _info is null
		? ""
		: _info.IdentityVerified
			? $"{_info.Security}. Certificate: {_info.CertificateSubject}"
			: $"{_info.Security}. The connection is encrypted, but nothing proves which machine is at the other end.";

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		if (_handle is not null)
		{
			_handle.Changed -= OnSessionChanged;
			_handle = null;
		}

		await _disposeCts.CancelAsync();
		await ReleaseAsync();
		_disposeCts.Dispose();
	}

	/// <summary>Events the page collected. They go straight to the session, which decides what to do with them.</summary>
	internal async Task HandleInputAsync(RdpInputEvent[] events)
	{
		IRdpChannel? channel = _channel;
		if (_disposed || channel is null || events.Length == 0)
		{
			return;
		}

		try
		{
			await channel.SendInputAsync(events, _disposeCts.Token);
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
		{
			// The view or the session closed while the input was on its way.
		}
		catch (Exception ex)
		{
			// The session reports the same failure through its state, which ends the tab.
			Logger.LogDebug(ex, "Sending RDP input failed.");
		}
	}

	/// <summary>The view was resized. Only a session set to resize the server acts on it.</summary>
	internal async Task HandleViewSizeAsync(int width, int height)
	{
		IRdpChannel? channel = _channel;
		if (_disposed || channel is null || _scaling != ScalingRemote || width <= 0 || height <= 0)
		{
			return;
		}

		try
		{
			if (!await channel.ResizeAsync(width, height, _disposeCts.Token))
			{
				Logger.LogDebug("The RDP server kept its {Width}x{Height} desktop.", _screenWidth, _screenHeight);
			}
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
		{
			// The view or the session closed while the resize was on its way.
		}
	}

	internal void HandleFullscreen(bool fullscreen)
	{
		if (!_disposed && _fullscreen != fullscreen)
		{
			_fullscreen = fullscreen;
			_ = InvokeAsync(StateHasChanged);
		}
	}

	protected override void OnParametersSet()
	{
		if (!ReferenceEquals(_handle, Session))
		{
			if (_handle is not null)
			{
				_handle.Changed -= OnSessionChanged;
			}

			_handle = Session;
			_handle.Changed += OnSessionChanged;
			RdpConnectionOptions options = RdpConnectionOptions.From(Session.Connection.Options);
			_scaling = ScalingOf(options.Scaling);
			_viewOnly = options.ViewOnly;
		}

		if (!ReferenceEquals(_attachedSession, Session.Session))
		{
			_pendingAttach = true;
		}

		if (_activeApplied != IsActive)
		{
			_pendingActive = true;
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (_disposed)
		{
			return;
		}

		try
		{
			if (_pendingAttach)
			{
				_pendingAttach = false;
				await SwitchSessionAsync();
			}

			if (_pendingActive && _instanceId != 0)
			{
				_pendingActive = false;
				_activeApplied = IsActive;
				await Interop.SetOptionsAsync(_instanceId, new Dictionary<string, object?>(StringComparer.Ordinal) { ["active"] = IsActive });
				if (IsActive)
				{
					// The canvas takes the keyboard as soon as its tab is the visible one.
					await Interop.FocusAsync(_instanceId);
				}
			}
		}
		catch (JSException ex)
		{
			Logger.LogError(ex, "The RDP view failed in JavaScript.");
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The circuit or WebView closed while the view was updating.
		}
	}

	private static string ScalingOf(RdpScalingMode mode) => mode switch
	{
		RdpScalingMode.Actual => ScalingActual,
		RdpScalingMode.Remote => ScalingRemote,
		_ => ScalingFit,
	};

	private static MokaVariant ToggleVariant(bool on) => on ? MokaVariant.Soft : MokaVariant.Text;

	private static MokaColor ToggleColor(bool on) => on ? MokaColor.Primary : MokaColor.Surface;

	private static string Pressed(bool on) => on ? "true" : "false";

	private async Task SwitchSessionAsync()
	{
		await ReleaseAsync();
		_attachedSession = Session.Session;
		_error = null;
		if (_attachedSession?.GetFeature<IRdpConnection>() is not { } connection)
		{
			_state = ScreenState.Idle;
			_screenWidth = 0;
			_screenHeight = 0;
			StateHasChanged();
			return;
		}

		_state = ScreenState.Starting;
		_info = connection.Info;
		_screenWidth = _info.Width;
		_screenHeight = _info.Height;
		StateHasChanged();

		try
		{
			_callbacks = DotNetObjectReference.Create(new RdpJsCallbacks(this));
			_instanceId = await Interop.CreateAsync(_screen, _callbacks, BuildOptions(), _disposeCts.Token);
			_activeApplied = IsActive;
			_pendingActive = false;
			_sink = new RdpPageSink(Interop, _instanceId, work => InvokeAsync(work), OnDesktopResized);

			_channel = await connection.AttachAsync(_sink, _disposeCts.Token);
			await _channel.SetViewOnlyAsync(_viewOnly, _disposeCts.Token);
			await _channel.StartAsync(_disposeCts.Token);
			_state = ScreenState.Running;
			WatchSession(_attachedSession);
			if (IsActive)
			{
				await Interop.FocusAsync(_instanceId);
			}
		}
		catch (OperationCanceledException)
		{
			// The view is going away.
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "Opening the RDP desktop failed.");
			_state = ScreenState.Failed;
			_error = ex is JSException ? "The remote desktop could not be started in this browser." : ex.Message;
			await ReleaseAsync();
		}

		StateHasChanged();
	}

	/// <summary>Turns the end of the connection into the overlay that says so, without waiting for the shell.</summary>
	private void WatchSession(IProtocolSession session) => _ = WatchSessionAsync(session);

	private async Task WatchSessionAsync(IProtocolSession session)
	{
		string? failure = null;
		try
		{
			await session.Completion;
		}
		catch (Exception ex)
		{
			failure = ex.Message;
		}

		if (_disposed || !ReferenceEquals(_attachedSession, session))
		{
			return;
		}

		await InvokeAsync(() =>
		{
			if (_disposed || !ReferenceEquals(_attachedSession, session))
			{
				return;
			}

			_state = failure is null ? ScreenState.Ended : ScreenState.Failed;
			_error = failure;
			StateHasChanged();
		});
	}

	private void OnDesktopResized(int width, int height)
	{
		if (_disposed || (_screenWidth == width && _screenHeight == height))
		{
			return;
		}

		_screenWidth = width;
		_screenHeight = height;
		_ = InvokeAsync(StateHasChanged);
	}

	private RdpJsOptions BuildOptions() => new()
	{
		Width = _screenWidth > 0 ? _screenWidth : RdpConnectionOptions.DefaultWidth,
		Height = _screenHeight > 0 ? _screenHeight : RdpConnectionOptions.DefaultHeight,
		Scaling = _scaling,
		ViewOnly = _viewOnly,
		Active = IsActive,
		ReleaseKeysWhenInactive = Settings.Get<RdpSettings>().ReleaseKeysWhenInactive,
		Background = "var(--moka-color-surface-2)",
	};

	private async Task ReleaseAsync()
	{
		if (_channel is { } channel)
		{
			_channel = null;
			try
			{
				await channel.DisposeAsync();
			}
			catch (Exception ex)
			{
				// Whatever the session makes of the detach, this view still has to drop its own screen.
				Logger.LogDebug(ex, "Detaching from the RDP session failed.");
			}
		}

		_sink?.Dispose();
		_sink = null;
		if (_instanceId != 0)
		{
			int id = _instanceId;
			_instanceId = 0;
			await Interop.TryDisposeInstanceAsync(id);
		}

		_callbacks?.Dispose();
		_callbacks = null;
	}

	private void OnSessionChanged() => _ = InvokeAsync(StateHasChanged);

	private async Task OnScalingChangedAsync(string value)
	{
		_scaling = value switch
		{
			ScalingActual => ScalingActual,
			ScalingRemote => ScalingRemote,
			_ => ScalingFit,
		};

		await SetJsOptionAsync("scaling", _scaling);
	}

	private async Task ToggleViewOnlyAsync()
	{
		_viewOnly = !_viewOnly;
		if (_channel is { } channel)
		{
			try
			{
				await channel.SetViewOnlyAsync(_viewOnly, _disposeCts.Token);
			}
			catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
			{
				// The session is closing.
			}
			catch (Exception ex)
			{
				// Turning view only on writes key releases to the server. This runs in an event handler, where an
				// exception would end a Blazor Server circuit, and a dropped connection ends the session anyway.
				Logger.LogDebug(ex, "Releasing the keys held on the RDP server failed.");
			}
		}

		await SetJsOptionAsync("viewOnly", _viewOnly);
	}

	private async Task SetJsOptionAsync(string name, object? value)
	{
		if (_instanceId == 0)
		{
			return;
		}

		try
		{
			await Interop.SetOptionsAsync(_instanceId, new Dictionary<string, object?>(StringComparer.Ordinal) { [name] = value });
			await Interop.FocusAsync(_instanceId);
		}
		catch (JSException ex)
		{
			Logger.LogDebug(ex, "Changing an RDP view option failed.");
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The view is closing.
		}
	}

	private void ShowKeysMenu(MouseEventArgs args)
	{
		if (IsRunning)
		{
			ContextMenu.Show(args, KeyItems());
		}
	}

	private List<MokaContextMenuItem> KeyItems() =>
	[
		Combo("Ctrl+Alt+Del", ["ControlLeft", "AltLeft", "Delete"], MokatermIcons.Keyboard),
		Combo("Ctrl+Shift+Esc", ["ControlLeft", "ShiftLeft", "Escape"]),
		Combo("Ctrl+Esc", ["ControlLeft", "Escape"]),
		Combo("Alt+Tab", ["AltLeft", "Tab"]),
		Combo("Alt+F4", ["AltLeft", "F4"]),
		Combo("Print Screen", ["PrintScreen"]),
		Combo("Windows", ["MetaLeft"]),
	];

	private MokaContextMenuItem Combo(string text, string[] codes, MokaIconDefinition? icon = null) => new()
	{
		Text = text,
		Icon = icon,
		Disabled = _viewOnly,
		OnClick = () => SendComboAsync(codes),
	};

	/// <summary>Presses the keys in order and lets them go in the other one, the way a hand would.</summary>
	private async Task SendComboAsync(string[] codes)
	{
		IRdpChannel? channel = _channel;
		if (!IsRunning || _viewOnly || channel is null)
		{
			return;
		}

		List<RdpInputEvent> events = new(codes.Length * 2);
		events.AddRange(codes.Select(RdpInputEvent.KeyDown));
		events.AddRange(Enumerable.Reverse(codes).Select(RdpInputEvent.KeyUp));

		try
		{
			await channel.SendInputAsync(events, _disposeCts.Token);
			await Interop.FocusAsync(_instanceId);
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException || JsModule.IsTeardown(ex))
		{
			// The view or the session closed while the keys were on their way.
		}
		catch (Exception ex)
		{
			// A menu action runs in an event handler, where an exception would end a Blazor Server circuit. A write
			// that failed because the connection dropped ends the session, which the view shows on its own.
			Logger.LogDebug(ex, "Sending an RDP key combination failed.");
		}
	}

	// Everything the wide toolbar shows as its own button, for the narrow toolbar's one menu.
	private void ShowActionsMenu(MouseEventArgs args)
	{
		if (!IsRunning)
		{
			return;
		}

		ContextMenu.Show(args,
		[
			new MokaContextMenuItem { Text = "Send keys", Icon = MokatermIcons.Keyboard, Disabled = _viewOnly, Children = KeyItems() },
			new MokaContextMenuItem
			{
				Text = "Save a picture of the desktop",
				Icon = MokaIcons.Action.Download,
				DividerBefore = true,
				OnClick = SaveScreenshotAsync,
			},
			new MokaContextMenuItem
			{
				Text = _fullscreen ? "Leave full screen" : "Full screen",
				Icon = MokatermIcons.Maximize,
				Checked = _fullscreen,
				OnClick = ToggleFullscreenAsync,
			},
		]);
	}

	/// <summary>Writes the desktop to a file the user picks. The picture is buffered first: the web host saves it later, when the browser starts the download.</summary>
	private async Task SaveScreenshotAsync()
	{
		if (!IsRunning)
		{
			return;
		}

		try
		{
			await using IJSStreamReference picture = await Interop.ScreenshotAsync(_instanceId, _disposeCts.Token);
			if (picture.Length == 0)
			{
				Interaction.Notify(NoticeSeverity.Warning, "There is nothing on the desktop to save yet.", "Screenshot");
				return;
			}

			using MemoryStream buffer = new();
			await using (Stream png = await picture.OpenReadStreamAsync(MaxScreenshotBytes, _disposeCts.Token))
			{
				await png.CopyToAsync(buffer, _disposeCts.Token);
			}

			bool saved = await LocalFiles.SaveFileAsync(
				ScreenshotName(),
				buffer.Length,
				async (destination, token) =>
				{
					buffer.Position = 0;
					await buffer.CopyToAsync(destination, token);
				},
				_disposeCts.Token);

			if (saved)
			{
				Interaction.Notify(NoticeSeverity.Success, "The desktop was saved as a PNG.", "Screenshot");
			}
		}
		catch (JSException ex)
		{
			Logger.LogWarning(ex, "Capturing the RDP desktop failed.");
			Interaction.Notify(NoticeSeverity.Warning, "The desktop could not be captured.", "Screenshot");
		}
		catch (Exception ex) when (ex is OperationCanceledException || JsModule.IsTeardown(ex))
		{
			// The view closed while the picture was on its way.
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Logger.LogWarning(ex, "Saving the RDP desktop failed.");
			Interaction.Notify(NoticeSeverity.Error, ex.Message, "Screenshot");
		}
	}

	private string ScreenshotName()
	{
		string stamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
		string name = string.Concat(Session.Title.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-')).Trim('-');
		return $"{(name.Length > 0 ? name : "desktop")} {stamp}.png";
	}

	private async Task ToggleFullscreenAsync()
	{
		if (!IsRunning)
		{
			return;
		}

		try
		{
			_fullscreen = await Interop.ToggleFullscreenAsync(_instanceId, _root);
			await Interop.FocusAsync(_instanceId);
		}
		catch (JSException ex)
		{
			Logger.LogDebug(ex, "Switching the RDP view to full screen failed.");
			Interaction.Notify(NoticeSeverity.Warning, "Full screen is not available here.", "Full screen");
		}
	}

	private Task RetryAsync()
	{
		_pendingAttach = true;
		_attachedSession = null;
		return InvokeAsync(StateHasChanged);
	}
}
