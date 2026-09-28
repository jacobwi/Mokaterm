using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Moka.Red.Core.Icons;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Settings.Pages.Keychain;

/// <summary>Shared credentials from <see cref="ICredentialStore"/>: add, edit, copy a password and delete.</summary>
public sealed partial class KeychainPage : VaultListPageBase<CredentialInfo>
{
	private bool _editorOpen;
	private bool _generateOpen;
	private CredentialInfo? _editing;
	private CredentialKind _newKind;

	[Inject]
	private ICredentialStore Store { get; set; } = default!;

	[Inject]
	private ISshKeyGenerator KeyGenerator { get; set; } = default!;

	[Inject]
	private IConnectionRepository Connections { get; set; } = default!;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	[Inject]
	private ISettingsService SettingsService { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	[Inject]
	private ILogger<KeychainPage> Logger { get; set; } = default!;

	protected override string LoadErrorMessage => "Credentials could not be loaded.";

	protected override void OnInitialized()
	{
		base.OnInitialized();
		Store.Changed += RequestReload;
	}

	protected override async ValueTask<IReadOnlyList<CredentialInfo>> LoadItemsAsync(CancellationToken cancellationToken)
	{
		IReadOnlyList<CredentialInfo> credentials = await Store.ListAsync(cancellationToken);

		// Same comparison MokaTable uses when it sorts the Name column itself.
		return [.. credentials
			.Where(credential => credential.IsShared)
			.OrderBy(credential => credential.Name, StringComparer.CurrentCulture)];
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			Store.Changed -= RequestReload;
		}

		base.Dispose(disposing);
	}

	private static string KindText(CredentialKind kind) => kind == CredentialKind.Password ? "Password" : "Private key";

	private static MokaIconDefinition KindIcon(CredentialKind kind) => kind == CredentialKind.Password ? MokatermIcons.Lock : MokatermIcons.Key;

	private static string DeleteMessage(string name, int logins) => logins switch
	{
		0 => $"Delete \"{name}\"? No saved logins use it.",
		1 => $"Delete \"{name}\"? One saved login uses it and will no longer have a saved secret.",
		_ => string.Create(CultureInfo.CurrentCulture, $"Delete \"{name}\"? {logins} saved logins use it and will no longer have a saved secret."),
	};

	private void AddCredential(CredentialKind kind)
	{
		_editing = null;
		_newKind = kind;
		_editorOpen = true;
	}

	private void EditCredential(CredentialInfo credential)
	{
		_editing = credential;
		_newKind = credential.Kind;
		_editorOpen = true;
	}

	private void GenerateKey() => _generateOpen = true;

	/// <summary>
	/// The public half of an OpenSSH key file is not encrypted, so this works whether or not the key has a
	/// passphrase. It is not a secret, which is why it goes to the plain clipboard.
	/// </summary>
	private async Task CopyPublicKeyAsync(CredentialInfo credential)
	{
		SshPublicKey? publicKey;
		try
		{
			using CredentialSecret secret = await Store.RevealAsync(credential.Id);
			if (secret.PrivateKey is not { } buffer)
			{
				Interaction.Notify(NoticeSeverity.Warning, "This credential has no saved key.");
				return;
			}

			publicKey = KeyGenerator.ReadPublicKey(buffer.RevealString());
		}
		catch (VaultLockedException)
		{
			Interaction.Notify(NoticeSeverity.Warning, VaultFailure.Locked("read keys"));
			return;
		}
		catch (KeyNotFoundException)
		{
			Interaction.Notify(NoticeSeverity.Warning, "This credential no longer exists.");
			return;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Reading the public key of a credential failed");
			Interaction.Notify(NoticeSeverity.Error, "The public key could not be read.");
			return;
		}

		if (publicKey is null)
		{
			Interaction.Notify(NoticeSeverity.Warning, "Only keys in OpenSSH format can show their public key here.");
			return;
		}

		try
		{
			await Clipboard.WriteTextAsync(publicKey.AuthorizedKeysLine);
		}
		catch (Exception ex)
		{
			// Browsers refuse clipboard writes from a page that is not focused or has no permission.
			Logger.LogWarning(ex, "Copying a public key to the clipboard failed");
			Interaction.Notify(NoticeSeverity.Error, "The public key could not be copied to the clipboard.");
			return;
		}

		Interaction.Notify(NoticeSeverity.Success, "Public key copied.");
	}

	private async Task CopyPasswordAsync(CredentialInfo credential)
	{
		try
		{
			string password;
			using (CredentialSecret secret = await Store.RevealAsync(credential.Id))
			{
				if (secret.Password is not { } buffer)
				{
					Interaction.Notify(NoticeSeverity.Warning, "This credential has no saved password.");
					return;
				}

				password = buffer.RevealString();
			}

			await Clipboard.WriteSecretAsync(password);
		}
		catch (VaultLockedException)
		{
			Interaction.Notify(NoticeSeverity.Warning, VaultFailure.Locked("copy passwords"));
			return;
		}
		catch (KeyNotFoundException)
		{
			Interaction.Notify(NoticeSeverity.Warning, "This credential no longer exists.");
			return;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Copying a saved password failed");
			Interaction.Notify(NoticeSeverity.Error, "The password could not be copied.");
			return;
		}

		int clearSeconds = SettingsService.Get<SecuritySettings>().ClipboardClearSeconds;
		Interaction.Notify(
			NoticeSeverity.Success,
			clearSeconds > 0
				? string.Create(CultureInfo.CurrentCulture, $"Password copied. The clipboard clears in {clearSeconds} s.")
				: "Password copied.");
	}

	private async Task DeleteAsync(CredentialInfo credential)
	{
		int logins;
		try
		{
			ConnectionCatalog catalog = await Connections.GetCatalogAsync();
			logins = catalog.Connections.Count(connection => connection.CredentialId == credential.Id);
		}
		catch (VaultLockedException)
		{
			Interaction.Notify(NoticeSeverity.Warning, VaultFailure.Locked("delete credentials"));
			return;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Counting the logins that use a credential failed");
			Interaction.Notify(NoticeSeverity.Error, "Could not check which logins use this credential.");
			return;
		}

		bool confirmed = (await Interaction.ConfirmAsync(new ConfirmPrompt
		{
			Title = "Delete credential",
			Message = DeleteMessage(credential.Name, logins),
			ConfirmText = "Delete",
			Destructive = true,
		})).Confirmed;

		if (!confirmed)
		{
			return;
		}

		try
		{
			await Store.DeleteAsync(credential.Id);
			Interaction.Notify(NoticeSeverity.Success, $"Deleted {credential.Name}.");
		}
		catch (VaultLockedException)
		{
			Interaction.Notify(NoticeSeverity.Warning, VaultFailure.Locked("delete credentials"));
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Deleting a credential failed");
			Interaction.Notify(NoticeSeverity.Error, "The credential could not be deleted.");
		}
	}
}
