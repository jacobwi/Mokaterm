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
using Mokaterm.Modules.Vnc.Interop;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Vnc.Components;

/// <summary>
/// The remote screen of a VNC session: a thin toolbar over a noVNC canvas. The canvas is created on the first render
/// and destroyed when the view unmounts; the session keeps the connection, so a view that comes back gets a fresh
/// screen without the user doing anything.
/// </summary>
public sealed partial class VncSessionView : SessionViewBase, IAsyncDisposable
{
	// A full screen PNG of a large desktop; well above what a 4K screen produces.
	private const long MaxScreenshotBytes = 64 * 1024 * 1024;

	private const string ScalingFit = "fit";
	private const string ScalingActual = "actual";
	private const string ScalingRemote = "remote";

	private readonly CancellationTokenSource _disposeCts = new();
	private ElementReference _root;
	private ElementReference _screen;
	private DotNetObjectReference<VncJsCallbacks>? _callbacks;
	private ISessionHandle? _handle;
	private IProtocolSession? _attachedSession;
	private IVncChannel? _channel;
	private VncPageSink? _sink;
	private VncConnectionInfo? _info;
	private ScreenState _state = ScreenState.Idle;
	private string? _error;
	private string _scaling = ScalingFit;
	private bool _viewOnly;
	private int _quality = VncConnectionOptions.DefaultQuality;
	private int _compression = VncConnectionOptions.DefaultCompression;
	private int _instanceId;
	private int _screenWidth;
	private int _screenHeight;
	private int _clipboardLength;
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
	private VncInterop Interop { get; set; } = default!;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	[Inject]
	private ILocalFileAccess LocalFiles { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private IMokaContextMenuService ContextMenu { get; set; } = default!;

	[Inject]
	private ILogger<VncSessionView> Logger { get; set; } = default!;

	private bool IsRunning => _state == ScreenState.Running && _instanceId != 0;

	private string DesktopName => string.IsNullOrWhiteSpace(_info?.DesktopName) ? Session.Title : _info.DesktopName;

	private string IdleDescription => SessionIdleText.For(Session.State, "Reconnect the session to open its screen again.");

	private string ReceiveClipboardTitle => _clipboardLength > 0
		? string.Create(CultureInfo.CurrentCulture, $"Copy the {_clipboardLength} characters the session last copied")
		: "The session has not copied any text";

	private string ScreenSizeText => _screenWidth > 0
		? string.Create(CultureInfo.InvariantCulture, $"{_screenWidth} x {_screenHeight}")
		: _info is { Width: > 0 } size ? string.Create(CultureInfo.InvariantCulture, $"{size.Width} x {size.Height}") : "";

	private string SecurityText => _info?.Security ?? "";

	private bool IsEncrypted => _info?.IsEncrypted == true;

	private string QualityText => string.Create(CultureInfo.InvariantCulture, $"Q{_quality} C{_compression}");

	private string QualityTitle => string.Create(CultureInfo.CurrentCulture, $"Picture quality {_quality}, compression {_compression}");

	/// <summary>Longest clipboard text, in characters, moved between the session and this machine in either direction.</summary>
	private int ClipboardLimit => Settings.Get<VncSettings>().Clamped().ClipboardKilobytes * 1024;

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

	/// <summary>Bytes the page produced. They go straight to the server, minus the handshake the relay answers.</summary>
	internal async Task HandleDataAsync(byte[] data)
	{
		IVncChannel? channel = _channel;
		if (_disposed || channel is null)
		{
			return;
		}

		try
		{
			await channel.SendAsync(data, _disposeCts.Token);
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
		{
			// The view or the session closed while the input was on its way.
		}
		catch (Exception ex)
		{
			// The relay reports the same failure through the session, which ends the tab's state.
			Logger.LogDebug(ex, "Sending VNC input failed.");
		}
	}

	internal void HandleOpened()
	{
		if (!_disposed && _state == ScreenState.Starting)
		{
			_state = ScreenState.Running;
			_ = InvokeAsync(StateHasChanged);
		}
	}

	internal void HandleClosed(bool clean)
	{
		if (_disposed || _state is ScreenState.Ended or ScreenState.Failed)
		{
			return;
		}

		bool opened = _state == ScreenState.Running;
		_state = clean ? ScreenState.Ended : ScreenState.Failed;
		_error = clean ? null : "The screen stopped because the session data could not be read.";
		_ = InvokeAsync(async () =>
		{
			// The page's protocol state is gone, so its connection goes too rather than feeding a closed screen until
			// the user tries again. A screen that never opened is still in the hands of SwitchSessionAsync.
			if (opened && !_disposed)
			{
				await ReleaseAsync();
			}

			StateHasChanged();
		});
	}

	internal void HandleDesktopName(string name)
	{
		if (_disposed || _info is null || _info.DesktopName == name)
		{
			return;
		}

		_info = _info with { DesktopName = name };
		_ = InvokeAsync(StateHasChanged);
	}

	internal void HandleScreenSize(int width, int height)
	{
		if (_disposed || (_screenWidth == width && _screenHeight == height))
		{
			return;
		}

		_screenWidth = width;
		_screenHeight = height;
		_ = InvokeAsync(StateHasChanged);
	}

	internal void HandleClipboard(int length)
	{
		if (!_disposed && _clipboardLength != length)
		{
			_clipboardLength = length;
			_ = InvokeAsync(StateHasChanged);
		}
	}

	/// <summary>The session copied more text than a session may move, so the page did not keep it.</summary>
	internal void HandleClipboardRefused(int length)
	{
		if (_disposed)
		{
			return;
		}

		string message = string.Create(
			CultureInfo.CurrentCulture,
			$"The session copied {length} characters; the limit for a session is {ClipboardLimit}, so the text was not kept.");
		_ = InvokeAsync(() => Interaction.Notify(NoticeSeverity.Warning, message, "Clipboard"));
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
			VncConnectionOptions options = VncConnectionOptions.From(Session.Connection.Options);
			_scaling = ScalingOf(options.Scaling);
			_viewOnly = options.ViewOnly;
			_quality = options.Quality;
			_compression = options.Compression;
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
			Logger.LogError(ex, "The VNC view failed in JavaScript.");
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The circuit or WebView closed while the view was updating.
		}
	}

	private static string ScalingOf(VncScalingMode mode) => mode switch
	{
		VncScalingMode.Actual => ScalingActual,
		VncScalingMode.Remote => ScalingRemote,
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
		_screenWidth = 0;
		_screenHeight = 0;
		_clipboardLength = 0;
		if (_attachedSession?.GetFeature<IVncConnection>() is not { } connection)
		{
			_state = ScreenState.Idle;
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
			_callbacks = DotNetObjectReference.Create(new VncJsCallbacks(this));
			_instanceId = await Interop.CreateAsync(_screen, _callbacks, BuildOptions(), _disposeCts.Token);
			_activeApplied = IsActive;
			_pendingActive = false;
			_sink = new VncPageSink(Interop, _instanceId, work => InvokeAsync(work));

			// The greeting starts the page's handshake, and its first answer arrives while that call is still running:
			// the channel has to be in place before it is sent.
			_channel = await connection.AttachAsync(_sink, _disposeCts.Token);
			await _channel.StartAsync(_disposeCts.Token);
			if (IsActive)
			{
				await Interop.FocusAsync(_instanceId);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogWarning(ex, "Opening the VNC screen failed.");
			_state = ScreenState.Failed;
			_error = ex is JSException ? "The remote screen could not be started in this browser." : ex.Message;
			await ReleaseAsync();
		}
		catch (OperationCanceledException)
		{
			// The view is going away.
		}

		StateHasChanged();
	}

	private VncJsOptions BuildOptions()
	{
		VncConnectionOptions options = VncConnectionOptions.From(Session.Connection.Options);
		return new VncJsOptions
		{
			Shared = options.Shared,
			ViewOnly = _viewOnly,
			Active = IsActive,
			Scaling = _scaling,
			Quality = _quality,
			Compression = _compression,
			ShowDotCursor = Settings.Get<VncSettings>().ShowDotCursor,
			ClipboardLimit = ClipboardLimit,
			Background = "var(--moka-color-surface-2)",
		};
	}

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
				Logger.LogDebug(ex, "Detaching from the VNC session failed.");
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

	private Task OnScalingChangedAsync(string value)
	{
		_scaling = value switch
		{
			ScalingActual => ScalingActual,
			ScalingRemote => ScalingRemote,
			_ => ScalingFit,
		};

		return SetJsOptionAsync("scaling", _scaling);
	}

	private Task ToggleViewOnlyAsync()
	{
		_viewOnly = !_viewOnly;
		return SetJsOptionAsync("viewOnly", _viewOnly);
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
			Logger.LogDebug(ex, "Changing a VNC view option failed.");
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
		Combo("Ctrl+Alt+Del", "ctrl-alt-del", MokatermIcons.Keyboard),
		Combo("Ctrl+Esc", "ctrl-esc"),
		Combo("Alt+Tab", "alt-tab"),
		Combo("Alt+F4", "alt-f4"),
		Combo("Print Screen", "print-screen"),
		Combo("Super", "super"),
	];

	private MokaContextMenuItem Combo(string text, string combo, MokaIconDefinition? icon = null) => new()
	{
		Text = text,
		Icon = icon,
		Disabled = _viewOnly,
		OnClick = () => SendComboAsync(combo),
	};

	private void ShowQualityMenu(MouseEventArgs args)
	{
		if (IsRunning)
		{
			ContextMenu.Show(args, QualityItems());
		}
	}

	private List<MokaContextMenuItem> QualityItems() =>
	[
		new()
		{
			Text = string.Create(CultureInfo.CurrentCulture, $"Quality ({_quality})"),
			Children = [.. Levels(_quality, level => SetQualityAsync(level))],
		},
		new()
		{
			Text = string.Create(CultureInfo.CurrentCulture, $"Compression ({_compression})"),
			Children = [.. Levels(_compression, level => SetCompressionAsync(level))],
		},
	];

	private static IEnumerable<MokaContextMenuItem> Levels(int current, Func<int, Task> select) =>
		Enumerable.Range(VncConnectionOptions.MinLevel, VncConnectionOptions.MaxLevel - VncConnectionOptions.MinLevel + 1)
			.Select(level => new MokaContextMenuItem
			{
				Text = LevelText(level),
				Checked = level == current,
				OnClick = () => select(level),
			});

	private static string LevelText(int level) => level switch
	{
		VncConnectionOptions.MinLevel => "0 (lowest)",
		VncConnectionOptions.MaxLevel => "9 (highest)",
		_ => level.ToString(CultureInfo.InvariantCulture),
	};

	private Task SetQualityAsync(int level)
	{
		_quality = level;
		return SetJsOptionAsync("quality", level);
	}

	private Task SetCompressionAsync(int level)
	{
		_compression = level;
		return SetJsOptionAsync("compression", level);
	}

	private Task SendComboAsync(string combo) => RunAsync(id => Interop.SendComboAsync(id, combo));

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
				Text = "Send the clipboard",
				Icon = MokaIcons.Content.Paste,
				Disabled = _viewOnly,
				OnClick = SendClipboardAsync,
			},
			new MokaContextMenuItem
			{
				Text = "Copy what the session copied",
				Icon = MokaIcons.Content.Copy,
				Disabled = _clipboardLength == 0,
				OnClick = ReceiveClipboardAsync,
			},
			new MokaContextMenuItem
			{
				Text = QualityTitle,
				Icon = MokatermIcons.Sliders,
				DividerBefore = true,
				Children = QualityItems(),
			},
			new MokaContextMenuItem { Text = "Save a picture of the screen", Icon = MokaIcons.Action.Download, OnClick = SaveScreenshotAsync },
			new MokaContextMenuItem
			{
				Text = _fullscreen ? "Leave full screen" : "Full screen",
				Icon = MokatermIcons.Maximize,
				Checked = _fullscreen,
				OnClick = ToggleFullscreenAsync,
			},
		]);
	}

	/// <summary>Writes the screen to a file the user picks. The picture is buffered first: the web host saves it later, when the browser starts the download.</summary>
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
				Interaction.Notify(NoticeSeverity.Warning, "There is nothing on the screen to save yet.", "Screenshot");
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
				Interaction.Notify(NoticeSeverity.Success, "The screen was saved as a PNG.", "Screenshot");
			}
		}
		catch (JSException ex)
		{
			Logger.LogWarning(ex, "Capturing the VNC screen failed.");
			Interaction.Notify(NoticeSeverity.Warning, "The screen could not be captured.", "Screenshot");
		}
		catch (Exception ex) when (ex is OperationCanceledException || JsModule.IsTeardown(ex))
		{
			// The view closed while the picture was on its way.
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Logger.LogWarning(ex, "Saving the VNC screen failed.");
			Interaction.Notify(NoticeSeverity.Error, ex.Message, "Screenshot");
		}
	}

	private string ScreenshotName()
	{
		string stamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
		string name = string.Concat(DesktopName.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-')).Trim('-');
		return $"{(name.Length > 0 ? name : "screen")} {stamp}.png";
	}

	private Task SendClipboardAsync() => RunClipboardAsync(async () =>
	{
		if (!IsRunning || _viewOnly)
		{
			return;
		}

		string? text = await Clipboard.ReadTextAsync(_disposeCts.Token);
		if (string.IsNullOrEmpty(text))
		{
			Interaction.Notify(NoticeSeverity.Warning, "There is no text on the clipboard to send.", "Clipboard");
			return;
		}

		int limit = ClipboardLimit;
		if (text.Length > limit)
		{
			Interaction.Notify(
				NoticeSeverity.Warning,
				string.Create(CultureInfo.CurrentCulture, $"The clipboard holds {text.Length} characters; the limit for a session is {limit}."),
				"Clipboard");
			return;
		}

		if (!IsRunning)
		{
			// The screen closed while the browser was reading the clipboard, which can wait on a permission prompt.
			return;
		}

		if (await Interop.SendClipboardAsync(_instanceId, text))
		{
			Interaction.Notify(NoticeSeverity.Success, "The clipboard was sent to the session.", "Clipboard");
			await Interop.FocusAsync(_instanceId);
		}
	});

	private Task ReceiveClipboardAsync() => RunClipboardAsync(async () =>
	{
		if (!IsRunning)
		{
			return;
		}

		int length = await Interop.ReceiveClipboardAsync(_instanceId);
		if (length < 0)
		{
			Interaction.Notify(NoticeSeverity.Warning, "The clipboard cannot be written here.", "Clipboard");
		}
		else if (length == 0)
		{
			Interaction.Notify(NoticeSeverity.Info, "The session has not copied any text yet.", "Clipboard");
		}
		else
		{
			Interaction.Notify(NoticeSeverity.Success, "The text copied in the session is on the clipboard.", "Clipboard");
		}
	});

	/// <summary>
	/// Clipboard actions run in event handlers, where an unhandled exception ends a Blazor Server circuit, and reading
	/// the clipboard can wait on a browser prompt long enough for the view to close meanwhile.
	/// </summary>
	private async Task RunClipboardAsync(Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (JSException ex)
		{
			Logger.LogDebug(ex, "A VNC clipboard action failed in JavaScript.");
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The view is closing.
		}
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
			Logger.LogDebug(ex, "Switching the VNC view to full screen failed.");
			Interaction.Notify(NoticeSeverity.Warning, "Full screen is not available here.", "Full screen");
		}
	}

	private Task RetryAsync()
	{
		_pendingAttach = true;
		_attachedSession = null;
		return InvokeAsync(StateHasChanged);
	}

	private async Task RunAsync(Func<int, ValueTask> call)
	{
		if (!IsRunning)
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
			Logger.LogDebug(ex, "A VNC view command failed in JavaScript.");
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The view is closing.
		}
	}
}
