using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Moka.Red.Core.Theming;
using Moka.Red.Feedback.Dialog;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// The root component of a session tool's own window, which the desktop host renders with the services of the window
/// that opened it. When the session or the vault goes away the window is closed from outside
/// (<see cref="SessionToolActions"/>); until then this says why there is nothing to show.
/// </summary>
public sealed partial class ToolWindowView : ComponentBase, IAsyncDisposable
{
	// The vault's idle timer counts minutes, so the page reports input at most this often, as the main window does.
	private static readonly TimeSpan ActivityReportInterval = TimeSpan.FromSeconds(20);

	private MokaTheme _theme = MokaTheme.Dark;
	private ISessionHandle? _session;
	private SessionToolDescriptor? _tool;
	private DotNetObjectReference<ToolWindowView>? _reference;
	private int? _listener;
	private bool _disposed;

	/// <summary>The session the tool works on.</summary>
	[Parameter]
	public Guid SessionId { get; set; }

	/// <summary>The tool's <see cref="SessionToolDescriptor.Key"/>.</summary>
	[Parameter]
	public string ToolKey { get; set; } = "";

	[Inject]
	private ISessionManager Sessions { get; set; } = default!;

	[Inject]
	private SessionToolActions Tools { get; set; } = default!;

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

	private static Dictionary<string, object> Parameters(ISessionHandle session) => new()
	{
		[nameof(SessionViewBase.Session)] = session,
		[nameof(SessionViewBase.IsActive)] = true,
	};

	private void Resolve()
	{
		ISessionHandle? session = Sessions.Find(SessionId);
		_tool = session is null ? null : Tools.For(session).FirstOrDefault(tool => tool.Key == ToolKey);
		Watch(session);
	}

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
