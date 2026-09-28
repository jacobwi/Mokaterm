using Mokaterm.UI.Common.Interop;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Presentation;

namespace Mokaterm.UI.Vault;

/// <summary>
/// The lock screen: master password or device unlock, throttling countdown, the reason the vault locked, and the
/// destructive reset for a forgotten password. The web host offers no reset here: anyone who reaches its page sees this
/// screen, so there a forgotten password is for whoever runs the server.
/// </summary>
public sealed partial class VaultUnlockScreen : ComponentBase, IDisposable
{
	private const string ResetWord = "RESET";
	private const string UnreadableVault = "The vault file could not be read. It is damaged or was written by a newer version of Mokaterm.";

	private ElementReference _form;
	private PromptBody? _resetBody;
	private string _password = "";
	private string? _error;
	private bool _busy;
	private bool _disposed;
	private DateTimeOffset? _retryUntil;
	private int _retrySeconds;
	private ITimer? _retryTimer;
	private bool _resetOpen;
	private bool _resetPrepared;
	private bool _resetting;
	private string _resetConfirmation = "";
	private string? _resetError;

	[Inject]
	private IVault Vault { get; set; } = default!;

	[Inject]
	private ISessionManager Sessions { get; set; } = default!;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private VaultScreenState ScreenState { get; set; } = default!;

	[Inject]
	private FormInterop Forms { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	[Inject]
	private IAppEnvironment Environment { get; set; } = default!;

	[Inject]
	private ILogger<VaultUnlockScreen> Logger { get; set; } = default!;

	private bool IsThrottled => _retrySeconds > 0;

	/// <summary>The vault refuses a reset while locked on the web host, so the screen does not offer one there.</summary>
	private bool CanReset => Environment.Kind != HostKind.Web;

	private bool ResetConfirmed => string.Equals(_resetConfirmation.Trim(), ResetWord, StringComparison.Ordinal);

	private string? LockReasonText
	{
		get
		{
			switch (Vault.LastLockReason)
			{
				case LockReason.Idle:
					int minutes = Settings.Get<SecuritySettings>().AutoLockMinutes;
					return minutes switch
					{
						<= 0 => "Locked after being idle",
						1 => "Locked after 1 minute idle",
						_ => string.Create(CultureInfo.CurrentCulture, $"Locked after {minutes} minutes idle"),
					};
				case LockReason.User:
					return "Locked by you";
				case LockReason.Background:
					return "Locked when the window was hidden";
				default:
					return null;
			}
		}
	}

	private string SessionsText => Sessions.Sessions.Count switch
	{
		0 => "",
		1 => "1 session keeps running while locked",
		int count => string.Create(CultureInfo.CurrentCulture, $"{count} sessions keep running while locked"),
	};

	public void Dispose()
	{
		_disposed = true;
		Sessions.SessionsChanged -= OnSessionsChanged;
		Interlocked.Exchange(ref _retryTimer, null)?.Dispose();
	}

	protected override void OnInitialized() => Sessions.SessionsChanged += OnSessionsChanged;

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender)
		{
			await Forms.PrepareAsync(_form, dialog: false, submitOnEnter: true);

			// Only when the app opens: after a user or idle lock, unlocking straight back would defeat the lock.
			if (Vault.IsDeviceUnlockEnabled
				&& Vault.LastLockReason is null or LockReason.Shutdown
				&& ScreenState.TryClaimAutomaticDeviceUnlock())
			{
				await UnlockWithDeviceAsync();
			}
		}

		if (_resetOpen && !_resetPrepared && _resetBody is not null)
		{
			_resetPrepared = true;
			await Forms.PrepareAsync(_resetBody.Element, dialog: true, submitOnEnter: true);
		}
	}

	private async Task UnlockAsync()
	{
		if (_busy || IsThrottled || _password.Length == 0)
		{
			return;
		}

		_busy = true;
		_error = null;
		string password = _password;
		_password = "";
		try
		{
			Apply(await Vault.UnlockAsync(password, CancellationToken.None));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Unlocking the vault failed.");
			_error = $"The vault could not be unlocked: {ex.Message}";
		}
		finally
		{
			_busy = false;
		}

		await RefocusAsync();
	}

	private async Task UnlockWithDeviceAsync()
	{
		if (_busy || IsThrottled)
		{
			return;
		}

		_busy = true;
		_error = null;
		StateHasChanged();
		try
		{
			Apply(await Vault.UnlockWithDeviceAsync(CancellationToken.None));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogWarning(ex, "Unlocking with the device key failed.");
			_error = "This device could not unlock the vault. Enter the master password.";
		}
		finally
		{
			_busy = false;
		}

		if (!_disposed)
		{
			StateHasChanged();
			await RefocusAsync();
		}
	}

	private void Apply(UnlockResult result)
	{
		switch (result.Status)
		{
			case UnlockStatus.Success:
				_error = null;
				break;
			case UnlockStatus.InvalidPassword:
				_error = "Wrong master password.";
				break;
			case UnlockStatus.Throttled:
				StartRetryCountdown(result.RetryAfter);
				break;
			case UnlockStatus.DeviceUnlockUnavailable:
				_error = "This device cannot unlock the vault. Enter the master password.";
				break;
			case UnlockStatus.VaultMissing:
				_error = "No vault was found. Restart Mokaterm to create a new one.";
				break;
			case UnlockStatus.Corrupted:
				_error = CanReset ? UnreadableVault : UnreadableVault + " Whoever runs this server can restore it or reset the vault.";
				break;
		}
	}

	private void StartRetryCountdown(TimeSpan retryAfter)
	{
		_retryUntil = Time.GetUtcNow() + retryAfter;
		UpdateRetrySeconds();
		ITimer timer = Time.CreateTimer(_ => _ = InvokeAsync(OnRetryTickAsync), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
		Interlocked.Exchange(ref _retryTimer, timer)?.Dispose();
	}

	private async Task OnRetryTickAsync()
	{
		if (_disposed)
		{
			return;
		}

		UpdateRetrySeconds();
		if (!IsThrottled)
		{
			Interlocked.Exchange(ref _retryTimer, null)?.Dispose();
		}

		StateHasChanged();
		if (!IsThrottled)
		{
			await RefocusAsync();
		}
	}

	private void UpdateRetrySeconds() =>
		_retrySeconds = _retryUntil is { } until ? Math.Max(0, (int)Math.Ceiling((until - Time.GetUtcNow()).TotalSeconds)) : 0;

	private async Task RefocusAsync()
	{
		if (!_disposed && Vault.Status == VaultStatus.Locked && !_resetOpen)
		{
			await Forms.FocusPreferredAsync(_form);
		}
	}

	private void OpenReset()
	{
		_resetConfirmation = "";
		_resetError = null;
		_resetPrepared = false;
		_resetOpen = true;
	}

	private void CloseReset()
	{
		if (!_resetting)
		{
			_resetOpen = false;
		}
	}

	private async Task ResetAsync()
	{
		if (!ResetConfirmed || _resetting || !CanReset)
		{
			return;
		}

		_resetting = true;
		_resetError = null;
		try
		{
			// Open sessions hold credentials for hosts that are about to disappear; starting over means closing them.
			await Sessions.CloseAllAsync();
			await Vault.ResetAsync(CancellationToken.None);
			_resetOpen = false;
		}
		catch (InvalidOperationException ex)
		{
			// The vault refused, and says why in words meant for the user.
			Logger.LogWarning("The vault refused to reset: {Reason}", ex.Message);
			_resetError = ex.Message;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Resetting the vault failed.");
			_resetError = $"The vault could not be reset: {ex.Message}";
		}
		finally
		{
			_resetting = false;
		}
	}

	private void OnSessionsChanged() => _ = InvokeAsync(() =>
	{
		if (!_disposed)
		{
			StateHasChanged();
		}
	});
}
