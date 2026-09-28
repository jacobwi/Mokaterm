using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.UI.Settings.Pages.Security;

/// <summary>Asks for the current and a new master password, then re-wraps the vault key through <see cref="IVault"/>.</summary>
public sealed partial class ChangeMasterPasswordDialog : DraftDialogBase
{
	private string _current = "";
	private string _new = "";
	private string _confirm = "";
	private string? _currentError;
	private string? _newError;
	private string? _confirmError;
	private string? _error;
	private bool _busy;

	[Inject]
	private IVault Vault { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private ILogger<ChangeMasterPasswordDialog> Logger { get; set; } = default!;

	protected override bool IsBusy => _busy;

	private static string ThrottledText(TimeSpan retryAfter)
	{
		int seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
		return seconds == 1
			? "Too many wrong passwords. Try again in 1 second."
			: string.Create(CultureInfo.CurrentCulture, $"Too many wrong passwords. Try again in {seconds} seconds.");
	}

	private static bool SecretsEqual(string left, string right)
	{
		using SecretBuffer leftBuffer = SecretBuffer.FromString(left);
		using SecretBuffer rightBuffer = SecretBuffer.FromString(right);
		return CryptographicOperations.FixedTimeEquals(leftBuffer.Span, rightBuffer.Span);
	}

	private async Task SubmitAsync()
	{
		if (_busy)
		{
			return;
		}

		_error = null;
		_currentError = _current.Length == 0 ? "Enter the current master password." : null;
		_newError = !VaultPasswordPolicy.IsAcceptable(_new) ? VaultPasswordPolicy.Requirement
			: SecretsEqual(_new, _current) ? "The new password is the same as the current one."
			: null;
		_confirmError = SecretsEqual(_new, _confirm) ? null : "The passwords don't match.";
		if (_currentError is not null || _newError is not null || _confirmError is not null)
		{
			return;
		}

		_busy = true;
		try
		{
			UnlockResult result = await Vault.ChangeMasterPasswordAsync(_current, _new);
			switch (result.Status)
			{
				case UnlockStatus.Success:
					Interaction.Notify(NoticeSeverity.Success, "Master password changed.");
					await CloseAsync();
					break;
				case UnlockStatus.Throttled:
					// Nothing was checked, so the password the user typed may well be right.
					_error = ThrottledText(result.RetryAfter);
					break;
				default:
					_currentError = "The current password is wrong.";
					break;
			}
		}
		catch (VaultLockedException)
		{
			_error = VaultFailure.Locked("change the master password");
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Changing the master password failed");
			_error = "The master password could not be changed.";
		}
		finally
		{
			_busy = false;
		}
	}

	// Strings cannot be wiped; dropping the references is the most a form field allows.
	protected override void Clear()
	{
		_current = "";
		_new = "";
		_confirm = "";
		_currentError = null;
		_newError = null;
		_confirmError = null;
		_error = null;
		_busy = false;
	}
}
