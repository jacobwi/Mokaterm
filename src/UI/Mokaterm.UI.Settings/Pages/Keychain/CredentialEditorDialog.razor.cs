using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.UI.Settings.Pages.Keychain;

/// <summary>Adds or edits a shared credential. On edit, blank secret fields keep what the vault already holds.</summary>
public sealed partial class CredentialEditorDialog : DraftDialogBase
{
	private string _name = "";
	private string _username = "";
	private string _notes = "";
	private string _password = "";
	private string? _privateKey;
	private string? _passphrase;
	private string _proxyPassword = "";
	private bool _forgetProxyPassword;
	private string? _nameError;
	private string? _secretError;
	private string? _error;
	private bool _saving;

	[Inject]
	private ICredentialStore Store { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	[Inject]
	private ILogger<CredentialEditorDialog> Logger { get; set; } = default!;

	/// <summary>The credential to edit, or null to add a new shared credential of <see cref="Kind"/>.</summary>
	[Parameter]
	public CredentialInfo? Credential { get; set; }

	[Parameter]
	public CredentialKind Kind { get; set; }

	protected override bool IsBusy => _saving;

	private bool IsNew => Credential is null;

	private bool IsPassword => (Credential?.Kind ?? Kind) == CredentialKind.Password;

	private string DialogTitle => (IsNew, IsPassword) switch
	{
		(true, true) => "Add password",
		(true, false) => "Add private key",
		(false, true) => "Edit password",
		(false, false) => "Edit private key",
	};

	private bool HasNewSecret => IsPassword ? _password.Length > 0 : !string.IsNullOrWhiteSpace(_privateKey);

	private bool HasSavedProxyPassword => Credential?.HasProxyPassword == true;

	private string ProxyPasswordHelp => _forgetProxyPassword
		? "The saved proxy password is deleted when you save."
		: HasSavedProxyPassword
			? "Saved. Leave blank to keep it."
			: "Only needed when a login using this credential dials through a proxy that asks for a login.";

	protected override void Load()
	{
		_name = Credential?.Name ?? "";
		_username = Credential?.Username ?? "";
		_notes = Credential?.Notes ?? "";
	}

	// Strings cannot be wiped; dropping the references is the most a form field allows.
	protected override void Clear()
	{
		_name = "";
		_username = "";
		_notes = "";
		_nameError = null;
		_secretError = null;
		_error = null;
		_password = "";
		_privateKey = null;
		_passphrase = null;
		_proxyPassword = "";
		_forgetProxyPassword = false;
	}

	private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	private async Task SaveAsync()
	{
		if (_saving)
		{
			return;
		}

		string name = _name.Trim();
		_error = null;
		_nameError = name.Length == 0 ? "Enter a name." : null;
		_secretError = !IsNew || HasNewSecret ? null
			: IsPassword ? "Enter the password."
			: "Paste or import a private key.";
		if (_nameError is not null || _secretError is not null)
		{
			return;
		}

		DateTimeOffset now = Time.GetUtcNow();
		CredentialInfo info = Credential is { } existing
			? existing with
			{
				Name = name,
				Username = NullIfBlank(_username),
				Notes = NullIfBlank(_notes),
				UpdatedAt = now,
			}
			: new CredentialInfo
			{
				Id = Guid.NewGuid(),
				Name = name,
				Kind = Kind,
				Username = NullIfBlank(_username),
				Notes = NullIfBlank(_notes),
				IsShared = true,
				CreatedAt = now,
				UpdatedAt = now,
			};

		_saving = true;
		try
		{
			await Store.SaveAsync(info, BuildSecret());
			Interaction.Notify(NoticeSeverity.Success, IsNew ? $"Added {name}." : $"Saved {name}.");
			await CloseAsync();
		}
		catch (CredentialValidationException ex)
		{
			_error = ex.Message;
		}
		catch (VaultLockedException)
		{
			_error = VaultFailure.Locked("save this credential");
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Saving a credential failed");
			_error = "The credential could not be saved.";
		}
		finally
		{
			_saving = false;
		}
	}

	private CredentialSecretInput? BuildSecret()
	{
		string? proxyPassword = _forgetProxyPassword ? "" : _proxyPassword.Length == 0 ? null : _proxyPassword;
		if (IsPassword)
		{
			return _password.Length == 0 && proxyPassword is null
				? null
				: new CredentialSecretInput { Password = _password.Length == 0 ? null : _password, ProxyPassword = proxyPassword };
		}

		bool replacesKey = !string.IsNullOrWhiteSpace(_privateKey);
		bool hasPassphrase = !string.IsNullOrEmpty(_passphrase);
		if (!replacesKey && !hasPassphrase)
		{
			return proxyPassword is null ? null : new CredentialSecretInput { ProxyPassword = proxyPassword };
		}

		return new CredentialSecretInput
		{
			PrivateKey = replacesKey ? _privateKey : null,
			// A replacement key must not inherit the old key's passphrase, and an empty string clears it.
			Passphrase = hasPassphrase ? _passphrase : "",
			ProxyPassword = proxyPassword,
		};
	}
}
