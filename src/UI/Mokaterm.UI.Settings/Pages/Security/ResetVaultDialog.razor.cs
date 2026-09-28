using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.Settings.Pages.Security;

/// <summary>Deletes the vault through <see cref="IVault.ResetAsync"/> once the user types the confirmation word.</summary>
public sealed partial class ResetVaultDialog : DraftDialogBase
{
	private const string ConfirmationWord = "RESET";

	private string _typed = "";
	private string? _error;
	private bool _busy;

	[Inject]
	private IVault Vault { get; set; } = default!;

	[Inject]
	private ISessionManager Sessions { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private ILogger<ResetVaultDialog> Logger { get; set; } = default!;

	protected override bool IsBusy => _busy;

	private bool IsConfirmed => string.Equals(_typed.Trim(), ConfirmationWord, StringComparison.Ordinal);

	private async Task ResetAsync()
	{
		if (_busy || !IsConfirmed)
		{
			return;
		}

		_busy = true;
		_error = null;
		try
		{
			// Same as the reset on the lock screen: open sessions hold credentials for logins that are about to be
			// deleted, and nothing else would close them.
			await Sessions.CloseAllAsync();
			await Vault.ResetAsync();
			Interaction.Notify(NoticeSeverity.Info, "The vault was reset. Choose a new master password to start again.");
			await CloseAsync();
		}
		catch (InvalidOperationException ex)
		{
			// The vault refused, for example on the web host after it locked in the meantime, and says why in words meant
			// for the user.
			Logger.LogWarning("The vault refused to reset: {Reason}", ex.Message);
			_error = ex.Message;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Resetting the vault failed");
			_error = "The vault could not be reset.";
		}
		finally
		{
			_busy = false;
		}
	}

	protected override void Clear()
	{
		_typed = "";
		_error = null;
		_busy = false;
	}
}
