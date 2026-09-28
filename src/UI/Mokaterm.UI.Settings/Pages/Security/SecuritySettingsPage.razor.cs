using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Settings.Pages.Security;

/// <summary>Settings page for <see cref="SecuritySettings"/> plus vault actions: lock, change password, device unlock and reset.</summary>
public sealed partial class SecuritySettingsPage : SettingsSectionBase<SecuritySettings>
{
	private static readonly IReadOnlyList<AutoLockPreset> AutoLockPresets =
	[
		new(0, "Never"),
		new(5, "5 min"),
		new(15, "15 min"),
		new(30, "30 min"),
		new(60, "1 h"),
	];

	private static readonly IReadOnlyList<PolicyOption> HostKeyPolicies =
	[
		new(HostKeyPolicy.Ask, "Ask", "Show the fingerprint of an unknown server and let you decide. A changed identity shows a loud warning."),
		new(HostKeyPolicy.AcceptNew, "Accept new", "Trust servers you have never connected to without asking. A changed identity is still refused."),
		new(HostKeyPolicy.Strict, "Strict", "Refuse every server that is not already in Known hosts."),
	];

	private bool _changePasswordOpen;
	private bool _resetVaultOpen;
	private bool _deviceUnlockBusy;

	[Inject]
	private IVault Vault { get; set; } = default!;

	[Inject]
	private IAppEnvironment AppEnvironment { get; set; } = default!;

	[Inject]
	private IServiceProvider Services { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private ILogger<SecuritySettingsPage> Logger { get; set; } = default!;

	private bool IsDesktop => AppEnvironment.Kind == HostKind.Desktop;

	private bool IsUnlocked => Vault.Status == VaultStatus.Unlocked;

	// Hosts that cannot protect a key per user do not register a protector, so the label falls back to generic wording.
	private string DeviceUnlockLabel => Services.GetService<IDeviceKeyProtector>() is { } protector
		? $"Unlock with {protector.Description}"
		: "Unlock on this device";

	private string VaultStatusLabel => Vault.Status switch
	{
		VaultStatus.Unlocked => "Unlocked",
		VaultStatus.Locked => "Locked",
		VaultStatus.Uninitialized => "Not set up",
		_ => "Loading",
	};

	private MokaColor VaultStatusColor => Vault.Status switch
	{
		VaultStatus.Unlocked => MokaColor.Success,
		VaultStatus.Locked => MokaColor.Warning,
		_ => MokaColor.Info,
	};

	private string VaultStatusDescription => Vault.Status switch
	{
		VaultStatus.Unlocked => "Connections, credentials and known hosts are decrypted in memory for this window.",
		VaultStatus.Locked => "Saved data stays encrypted until the master password is entered.",
		VaultStatus.Uninitialized => "No vault exists yet. It is created with the first master password.",
		_ => "Reading the vault.",
	};

	protected override void OnInitialized()
	{
		base.OnInitialized();
		Vault.StatusChanged += OnVaultStatusChanged;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			Vault.StatusChanged -= OnVaultStatusChanged;
		}

		base.Dispose(disposing);
	}

	private void OnVaultStatusChanged(VaultStatus status) => _ = InvokeAsync(StateHasChanged);

	private Task SetAutoLockAsync(int minutes) => SaveAsync(s => s with { AutoLockMinutes = minutes });

	private Task SaveAsync(Func<SecuritySettings, SecuritySettings> change) => UpdateAsync(s => change(s).Clamped());

	private void LockNow() => Vault.Lock(LockReason.User);

	private async Task SetDeviceUnlockAsync(bool enabled)
	{
		_deviceUnlockBusy = true;
		try
		{
			await Vault.SetDeviceUnlockAsync(enabled);
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Changing device unlock failed");
			Interaction.Notify(
				NoticeSeverity.Error,
				enabled ? "Device unlock could not be turned on." : "Device unlock could not be turned off.");
		}
		finally
		{
			_deviceUnlockBusy = false;
		}
	}

	private sealed record AutoLockPreset(int Minutes, string Label);

	private sealed record PolicyOption(HostKeyPolicy Value, string Title, string Description);
}
