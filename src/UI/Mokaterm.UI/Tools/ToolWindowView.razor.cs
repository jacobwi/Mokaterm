using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Moka.Red.Core.Theming;
using Moka.Red.Feedback.Dialog;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tools;

/// <summary>
/// The root component of a tool's own window, a session tool's or an app tool's, which the desktop host renders with
/// the services of the window that opened it. When the session or the vault goes away the window is closed from
/// outside (<see cref="ShellToolWindows"/>); until then this says why there is nothing to show.
/// </summary>
public sealed partial class ToolWindowView : ComponentBase, IAsyncDisposable
{
	// The vault's idle timer counts minutes, so the page reports input at most this often, as the main window does.
	private static readonly TimeSpan ActivityReportInterval = TimeSpan.FromSeconds(20);

	private MokaTheme _theme = MokaTheme.Dark;
	private ISessionHandle? _session;
	private SessionToolDescriptor? _sessionTool;
	private AppToolDescriptor? _appTool;
	private DotNetObjectReference<ToolWindowView>? _reference;
	private int? _listener;
	private bool _disposed;

	/// <summary>The session tool or app tool the window shows.</summary>
	[Parameter, EditorRequired]
	public ToolWindowTarget Target { get; set; } = default!;

	[Inject]
	private ISessionManager Sessions { get; set; } = default!;

	[Inject]
	private SessionToolActions SessionTools { get; set; } = default!;

	[Inject]
	private AppToolActions AppTools { get; set; } = default!;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private IVault Vault { get; set; } = default!;

	[Inject]
	private ShellInterop Interop { get; set; } = default!;

	[Inject]
	private IMokaDialogService Dialogs { get; set; } = default!;

	/// <summary>Input in this window keeps the vault from locking for idleness, the way input in the main window does.</summary>
	[JSInvokable]
	public void OnActivity()
	{
		if (Vault.Status == VaultStatus.Unlocked)
		{
			Vault.ReportActivity();
		}
	}

	public async ValueTask DisposeAsync()
	{
		_disposed = true;
		Settings.Changed -= OnSettingsChanged;
		Sessions.SessionsChanged -= OnSessionsChanged;
		Watch(null);
		if (_listener is { } listener)
		{
			_listener = null;
			_ = await Interop.DetachShellAsync(listener);
		}

		_reference?.Dispose();
	}

	protected override void OnInitialized()
	{
		_theme = ThemeFactory.Create(Settings.Get<AppearanceSettings>());
		Settings.Changed += OnSettingsChanged;
		Sessions.SessionsChanged += OnSessionsChanged;
		Resolve();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (!firstRender)
		{
			return;
		}

		// Only the activity report: no shortcuts (this window has no shell to run them), no hidden-window lock (hiding a
		// tool window is not hiding the app) and no leave prompt (the window's own close is the way out).
		_reference = DotNetObjectReference.Create(this);
		try
		{
			int listener = await Interop.AttachShellAsync(
				_reference,
				new ShellListenerOptions([], (int)ActivityReportInterval.TotalMilliseconds, WatchVisibility: false, ConfirmLeave: false));
			if (_disposed)
			{
				_ = await Interop.DetachShellAsync(listener);
				return;
			}

			_listener = listener;
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			// The window is closing as it opens; there is nothing left to report activity for.
		}
	}

	private void Resolve()
	{
		if (Target.SessionId is { } sessionId)
		{
			ISessionHandle? session = Sessions.Find(sessionId);
			_sessionTool = session is null ? null : SessionTools.Find(session, Target.ToolKey);
			Watch(session);
		}
		else
		{
			_appTool = AppTools.Find(Target.ToolKey);
		}
	}

	// The frame's body follows the session too: a tool that needs the connection gives way to a note while it is down.
	private void Watch(ISessionHandle? session)
	{
		if (ReferenceEquals(_session, session))
		{
			return;
		}

		if (_session is not null)
		{
			_session.Changed -= OnSessionChanged;
		}

		_session = session;
		if (session is not null)
		{
			session.Changed += OnSessionChanged;
		}
	}

	private void CloseServiceDialogs() => Dialogs.CloseAll();

	private void OnSessionChanged() => _ = InvokeAsync(StateHasChanged);

	private void OnSessionsChanged() => _ = InvokeAsync(() =>
	{
		Resolve();
		StateHasChanged();
	});

	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey == AppearanceSettings.SectionKey)
		{
			_ = InvokeAsync(() =>
			{
				_theme = ThemeFactory.Create(Settings.Get<AppearanceSettings>());
				StateHasChanged();
			});
		}
	}
}
