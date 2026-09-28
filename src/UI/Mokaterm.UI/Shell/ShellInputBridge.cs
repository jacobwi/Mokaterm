using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.UI.Shell;

/// <summary>
/// Receives the window-level callbacks from <c>shell.js</c>: global shortcuts, input activity for the vault's idle
/// timer, and the window being hidden. Attached while the shell is mounted, which is exactly while the vault is unlocked.
/// </summary>
internal sealed class ShellInputBridge : IAsyncDisposable
{
	// The idle timeout is measured in minutes, so one report per interval keeps the timer accurate enough without a
	// round trip for every key press.
	private static readonly TimeSpan ActivityReportInterval = TimeSpan.FromSeconds(20);

	private readonly ShellInterop _interop;
	private readonly ShellCommandRegistry _commands;
	private readonly IVault _vault;
	private readonly ISettingsService _settings;
	private readonly IAppEnvironment _environment;
	private readonly ISessionManager _sessions;
	private readonly ILogger<ShellInputBridge> _logger;
	private DotNetObjectReference<ShellInputBridge>? _reference;
	private int? _handle;

	public ShellInputBridge(
		ShellInterop interop,
		ShellCommandRegistry commands,
		IVault vault,
		ISettingsService settings,
		IAppEnvironment environment,
		ISessionManager sessions,
		ILogger<ShellInputBridge> logger)
	{
		_interop = interop;
		_commands = commands;
		_vault = vault;
		_settings = settings;
		_environment = environment;
		_sessions = sessions;
		_logger = logger;
	}

	// A reload or a closed tab ends a Blazor Server circuit, and the circuit's sessions close with it.
	private bool ConfirmLeave => _environment.Kind == HostKind.Web;

	public async Task AttachAsync()
	{
		if (_reference is not null)
		{
			return;
		}

		DotNetObjectReference<ShellInputBridge> reference = DotNetObjectReference.Create(this);
		_reference = reference;
		ShellShortcuts.Apply(_settings.Get<ShortcutSettings>());
		_settings.Changed += OnSettingsChanged;
		ShellListenerOptions options = new(
			ShellShortcuts.Bindings,
			(int)ActivityReportInterval.TotalMilliseconds,
			WatchVisibility: _environment.Kind == HostKind.Desktop,
			ConfirmLeave);

		int handle;
		try
		{
			handle = await _interop.AttachShellAsync(reference, options);
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The window or circuit closed while the shell was mounting.
			return;
		}

		// The shell unmounted (the vault locked) while the script was attaching. Nothing would ever detach these listeners,
		// and the next unlock would add a second set next to them.
		if (!ReferenceEquals(_reference, reference))
		{
			await _interop.DetachShellAsync(handle);
			return;
		}

		_handle = handle;
		if (ConfirmLeave)
		{
			_sessions.SessionsChanged += OnSessionsChanged;
			OnSessionsChanged();
		}
	}

	public async Task DetachAsync()
	{
		_sessions.SessionsChanged -= OnSessionsChanged;
		_settings.Changed -= OnSettingsChanged;

		// Both are cleared before the await: a shell that mounts while this one is still detaching must attach its own
		// listeners instead of finding a reference that is about to be disposed and skipping the attach.
		DotNetObjectReference<ShellInputBridge>? reference = _reference;
		int? handle = _handle;
		_reference = null;
		_handle = null;
		if (handle is { } attached)
		{
			await _interop.DetachShellAsync(attached);
		}

		reference?.Dispose();
	}

	/// <summary>
	/// Turns the global shortcuts off while the settings page records a gesture, so the keys pressed there do not run the
	/// commands they belong to right now.
	/// </summary>
	public async Task SetShortcutsEnabledAsync(bool enabled)
	{
		if (_handle is { } handle)
		{
			_ = await _interop.SetShortcutsEnabledAsync(handle, enabled);
		}
	}

	[JSInvokable]
	public Task OnShortcut(string commandId) => _commands.ExecuteAsync(commandId);

	[JSInvokable]
	public void OnActivity()
	{
		if (_vault.Status == VaultStatus.Unlocked)
		{
			_vault.ReportActivity();
		}
	}

	[JSInvokable]
	public void OnHidden()
	{
		if (_environment.Kind != HostKind.Desktop
			|| _vault.Status != VaultStatus.Unlocked
			|| !_settings.Get<SecuritySettings>().LockWhenHidden)
		{
			return;
		}

		_logger.LogInformation("Locking the vault because the window was hidden.");
		_vault.Lock(LockReason.Background);
	}

	public async ValueTask DisposeAsync() => await DetachAsync();

	// Raised on any thread. The listeners carry the gestures they were attached with, so changed bindings mean a fresh
	// attach; the shell is mounted throughout, so nothing else has to know.
	private async void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey != ShortcutSettings.SectionKey || _reference is null)
		{
			return;
		}

		try
		{
			await DetachAsync();
			await AttachAsync();
			_commands.RefreshShortcutLabels();
		}
		catch (Exception ex)
		{
			// An event handler: anything escaping here would take the process down.
			_logger.LogDebug(ex, "Reattaching the shell listeners after a shortcut change failed.");
		}
	}

	// Raised on any thread; each call sends the current state, so a late one never leaves a stale answer behind.
	private async void OnSessionsChanged()
	{
		if (_handle is not { } handle)
		{
			return;
		}

		try
		{
			_ = await _interop.SetConfirmLeaveAsync(handle, _sessions.Sessions.Count > 0);
		}
		catch (Exception ex)
		{
			// An event handler: anything escaping here would take the process down.
			_logger.LogDebug(ex, "Updating the leave-page confirmation failed.");
		}
	}
}
