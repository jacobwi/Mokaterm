using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Security;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.UI.Settings.Pages.Keychain;

/// <summary>
/// Makes a key pair and puts it in the keychain like any other private key. The public key shows as soon as it
/// exists so it can be copied to a server; the private key only reaches a file when the user asks for it twice.
/// </summary>
public sealed partial class GenerateKeyDialog : DraftDialogBase
{
	private static readonly KeyTypeOption[] Options =
	[
		new(SshKeyType.Ed25519, "Ed25519", "Small, fast and what OpenSSH suggests today."),
		new(SshKeyType.Rsa3072, "RSA 3072", "For servers that still refuse anything but RSA."),
		new(SshKeyType.Rsa4096, "RSA 4096", "The same, with a longer key."),
	];

	private string _name = "";
	private string _comment = "";
	private string _passphrase = "";
	private string _passphraseAgain = "";
	private SshKeyType _type = SshKeyType.Ed25519;
	private GeneratedSshKey? _generated;
	private string? _nameError;
	private string? _passphraseError;
	private string? _error;
	private bool _busy;

	[Inject]
	private ISshKeyGenerator Generator { get; set; } = default!;

	[Inject]
	private ICredentialStore Store { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private IServiceProvider Services { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	[Inject]
	private ILogger<GenerateKeyDialog> Logger { get; set; } = default!;

	protected override bool IsBusy => _busy;

	private bool CanExport => Services.GetService<ILocalFileAccess>() is not null;

	// Strings cannot be wiped; dropping the references is the most a form allows. The generated key goes with them.
	protected override void Clear()
	{
		_name = "";
		_comment = "";
		_passphrase = "";
		_passphraseAgain = "";
		_type = SshKeyType.Ed25519;
		_generated = null;
		_nameError = null;
		_passphraseError = null;
		_error = null;
		_busy = false;
	}

	private void SetName(string value)
	{
		_name = value;
		_nameError = null;
	}

	// The key material and the comment are baked into the file, so any change makes the shown key stale.
	private void SetType(SshKeyType value)
	{
		_type = value;
		Invalidate();
	}

	private void SetComment(string value)
	{
		_comment = value;
		Invalidate();
	}

	private void SetPassphrase(string value)
	{
		_passphrase = value;
		Invalidate();
	}

	private void SetPassphraseAgain(string value)
	{
		_passphraseAgain = value;
		Invalidate();
	}

	private void Invalidate()
	{
		_generated = null;
		_passphraseError = null;
		_error = null;
	}

	private async Task GenerateAsync()
	{
		if (_busy)
		{
			return;
		}

		_error = null;
		_nameError = _name.Trim().Length == 0 ? "Enter a name." : null;
		_passphraseError = _passphrase == _passphraseAgain ? null : "The two passphrases are different.";
		if (_nameError is not null || _passphraseError is not null)
		{
			return;
		}

		_busy = true;
		try
		{
			_generated = await Generator.GenerateAsync(new SshKeyRequest
			{
				Type = _type,
				Comment = _comment,
				Passphrase = _passphrase.Length == 0 ? null : _passphrase,
			});
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Generating a key failed");
			_error = "The key could not be generated.";
		}
		finally
		{
			_busy = false;
		}
	}

	private async Task SaveAsync()
	{
		if (_busy || _generated is not { } key)
		{
			return;
		}

		string name = _name.Trim();
		_nameError = name.Length == 0 ? "Enter a name." : null;
		if (_nameError is not null)
		{
			return;
		}

		DateTimeOffset now = Time.GetUtcNow();
		_busy = true;
		try
		{
			await Store.SaveAsync(
				new CredentialInfo
				{
					Id = Guid.NewGuid(),
					Name = name,
					Kind = CredentialKind.PrivateKey,
					IsShared = true,
					Notes = "Generated in Mokaterm.",
					CreatedAt = now,
					UpdatedAt = now,
				},
				new CredentialSecretInput
				{
					PrivateKey = key.PrivateKey,
					Passphrase = key.HasPassphrase ? _passphrase : null,
				});

			Interaction.Notify(NoticeSeverity.Success, $"Added {name}.");
			await CloseAsync();
		}
		catch (CredentialValidationException ex)
		{
			_error = ex.Message;
		}
		catch (VaultLockedException)
		{
			_error = VaultFailure.Locked("save this key");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Saving a generated key failed");
			_error = "The key could not be saved.";
		}
		finally
		{
			_busy = false;
		}
	}

	private async Task ExportAsync()
	{
		if (_busy || _generated is not { } key || Services.GetService<ILocalFileAccess>() is not { } files)
		{
			return;
		}

		bool confirmed = (await Interaction.ConfirmAsync(new ConfirmPrompt
		{
			Title = "Export the private key",
			Message = key.HasPassphrase
				? $"Write the private key to a file? It stays encrypted with your passphrase, and anyone who learns that passphrase can log in as {key.PublicKey.Comment}."
				: "Write the private key to a file? It has no passphrase, so anyone who gets the file can log in with it.",
			ConfirmText = "Export",
			Destructive = true,
		})).Confirmed;

		if (!confirmed)
		{
			return;
		}

		// OpenSSH refuses a key file with CRLF, so the text goes out exactly as it was written.
		byte[] content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(key.PrivateKey);
		_busy = true;
		try
		{
			bool saved = await files.SaveFileAsync(
				key.SuggestedFileName,
				content.Length,
				async (stream, cancellationToken) => await stream.WriteAsync(content, cancellationToken));

			if (saved)
			{
				Interaction.Notify(NoticeSeverity.Success, "The private key was exported. Keep the file somewhere safe.");
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Exporting a generated key failed");
			Interaction.Notify(NoticeSeverity.Error, "The key could not be exported.");
		}
		finally
		{
			_busy = false;
		}
	}

	private sealed record KeyTypeOption(SshKeyType Value, string Title, string Description);
}
