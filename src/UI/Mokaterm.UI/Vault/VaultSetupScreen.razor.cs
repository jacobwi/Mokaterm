using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.UI.Vault;

/// <summary>First run: choose the master password, and optionally let this device unlock the vault.</summary>
public sealed partial class VaultSetupScreen : ComponentBase
{
	private ElementReference _form;
	private string _password = "";
	private string _confirmation = "";
	private bool _deviceUnlock;
	private bool _busy;
	private string? _error;

	[Inject]
	private IVault Vault { get; set; } = default!;

	[Inject]
	private FormInterop Forms { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private ILogger<VaultSetupScreen> Logger { get; set; } = default!;

	private bool CanCreate =>
		!_busy && VaultPasswordPolicy.IsAcceptable(_password) && string.Equals(_password, _confirmation, StringComparison.Ordinal);

	// Only complain once the confirmation is as long as the password, not after every keystroke.
	private string? ConfirmationError =>
		_confirmation.Length >= _password.Length && _confirmation.Length > 0 && !string.Equals(_password, _confirmation, StringComparison.Ordinal)
			? "The passwords do not match."
			: null;

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender)
		{
			await Forms.PrepareAsync(_form, dialog: false, submitOnEnter: true);
		}
	}

	private async Task CreateAsync()
	{
		if (!CanCreate)
		{
			return;
		}

		_busy = true;
		_error = null;
		string password = _password;
		bool enableDeviceUnlock = _deviceUnlock && Vault.IsDeviceUnlockAvailable;
		try
		{
			// Creating the vault unlocks it, which unmounts this screen; everything after this must not depend on it.
			await Vault.CreateAsync(password, CancellationToken.None);
			_password = "";
			_confirmation = "";
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Creating the vault failed.");
			_error = $"The vault could not be created: {ex.Message}";
			_busy = false;
			return;
		}

		if (!enableDeviceUnlock)
		{
			return;
		}

		try
		{
			await Vault.SetDeviceUnlockAsync(true, CancellationToken.None);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogWarning(ex, "Turning on device unlock failed.");
			Interaction.Notify(
				NoticeSeverity.Warning,
				"The vault was created, but unlocking with this device could not be turned on. You can try again in Settings.",
				"Device unlock");
		}
	}
}
