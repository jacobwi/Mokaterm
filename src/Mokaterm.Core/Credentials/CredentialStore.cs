using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;
using Mokaterm.Core.Security;
using Mokaterm.Core.Storage;
using Mokaterm.Core.Validation;

namespace Mokaterm.Core.Credentials;

/// <summary>
/// Passwords and private keys in the encrypted <c>credentials</c> document. Each secret field is sealed again on its own
/// under associated data naming the credential and field, so a sealed value cannot be moved to another credential.
/// </summary>
internal sealed class CredentialStore : ICredentialStore, IDisposable
{
	private const string PasswordField = "password";
	private const string PrivateKeyField = "privateKey";
	private const string PassphraseField = "passphrase";
	private const string ProxyPasswordField = "proxyPassword";

	private readonly VaultDocumentCache<CredentialsDocument> _document;
	private readonly IVaultCipher _cipher;
	private readonly TimeProvider _timeProvider;
	private readonly IPrivateKeyInspector? _inspector;

	public CredentialStore(
		VaultDataStore store,
		IVault vault,
		IVaultCipher cipher,
		VaultDocumentUpdateLocks updateLocks,
		TimeProvider timeProvider,
		IPrivateKeyInspector? inspector = null)
	{
		_document = new VaultDocumentCache<CredentialsDocument>(CredentialsDocument.DocumentName, static () => new CredentialsDocument(), store, vault, updateLocks);
		_document.ExternalChange += OnExternalChange;
		_cipher = cipher;
		_timeProvider = timeProvider;
		_inspector = inspector;
	}

	public event Action? Changed;

	public async ValueTask<IReadOnlyList<CredentialInfo>> ListAsync(CancellationToken cancellationToken = default)
	{
		CredentialsDocument document = await _document.GetAsync(cancellationToken);
		return [.. document.Credentials.Select(credential => credential.Info)];
	}

	public async ValueTask<CredentialInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default)
	{
		CredentialsDocument document = await _document.GetAsync(cancellationToken);
		return Find(document, id)?.Info;
	}

	public async ValueTask<CredentialInfo> SaveAsync(CredentialInfo info, CredentialSecretInput? secret, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(info);
		string name = InputText.Require(info.Name, "A credential needs a name.", nameof(info));
		if (!Enum.IsDefined(info.Kind))
		{
			throw new ArgumentException("Unknown credential kind.", nameof(info));
		}

		Guid id = info.Id == Guid.Empty ? Guid.NewGuid() : info.Id;

		// Key inspection can run a slow passphrase KDF; keep it and the encryption off the caller's (UI) thread.
		CredentialInfo saved = await Task.Run(
			() => _document.UpdateAsync<CredentialInfo>(
				document =>
				{
					StoredCredential? existing = Find(document, id);
					if (existing is null && secret is null)
					{
						throw new ArgumentNullException(nameof(secret), "A new credential needs a secret.");
					}

					StoredCredential stored = Build(id, name, info, existing, secret);
					StoredCredential[] credentials = existing is null
						? [.. document.Credentials, stored]
						: [.. document.Credentials.Select(credential => credential.Info.Id == id ? stored : credential)];
					return (document with { Credentials = credentials }, stored.Info);
				},
				cancellationToken),
			cancellationToken);

		Changed?.Invoke();
		return saved;
	}

	public async ValueTask<CredentialSecret> RevealAsync(Guid id, CancellationToken cancellationToken = default)
	{
		CredentialsDocument document = await _document.GetAsync(cancellationToken);
		StoredCredential stored = Find(document, id) ?? throw new KeyNotFoundException("No credential with that id exists.");

		SecretBuffer? password = null;
		SecretBuffer? privateKey = null;
		SecretBuffer? passphrase = null;
		SecretBuffer? proxyPassword = null;
		try
		{
			password = OpenSecret(id, PasswordField, stored.Password);
			privateKey = OpenSecret(id, PrivateKeyField, stored.PrivateKey);
			passphrase = OpenSecret(id, PassphraseField, stored.Passphrase);
			proxyPassword = OpenSecret(id, ProxyPasswordField, stored.ProxyPassword);
			return new CredentialSecret(stored.Info.Kind, password, privateKey, passphrase, proxyPassword);
		}
		catch
		{
			password?.Dispose();
			privateKey?.Dispose();
			passphrase?.Dispose();
			proxyPassword?.Dispose();
			throw;
		}
	}

	public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
	{
		bool removed = await _document.UpdateAsync<bool>(
			document => Find(document, id) is null
				? (null, false)
				: (document with { Credentials = [.. document.Credentials.Where(credential => credential.Info.Id != id)] }, true),
			cancellationToken);

		if (removed)
		{
			Changed?.Invoke();
		}
	}

	public void Dispose()
	{
		_document.ExternalChange -= OnExternalChange;
		_document.Dispose();
	}

	private static StoredCredential? Find(CredentialsDocument document, Guid id) =>
		document.Credentials.FirstOrDefault(credential => credential.Info.Id == id);

	private static string ValidationMessage(PrivateKeyInspection inspection) => inspection.Status switch
	{
		PrivateKeyStatus.PassphraseRequired => "This private key is encrypted. Enter its passphrase.",
		PrivateKeyStatus.WrongPassphrase => "The passphrase does not unlock this private key.",
		PrivateKeyStatus.Unsupported => string.IsNullOrWhiteSpace(inspection.Error) ? "This key type is not supported." : inspection.Error,
		_ => string.IsNullOrWhiteSpace(inspection.Error) ? "This is not a valid private key." : inspection.Error,
	};

	private StoredCredential Build(Guid id, string name, CredentialInfo info, StoredCredential? existing, CredentialSecretInput? secret)
	{
		bool hasPassword = HasValue(existing?.Password, secret?.Password);
		bool hasPrivateKey = HasValue(existing?.PrivateKey, secret?.PrivateKey);
		if (info.Kind == CredentialKind.Password && !hasPassword)
		{
			throw new CredentialValidationException("Enter a password for this credential.");
		}

		if (info.Kind == CredentialKind.PrivateKey && !hasPrivateKey)
		{
			throw new CredentialValidationException("Add a private key for this credential.");
		}

		(string? keyAlgorithm, string? keyFingerprint) = DescribeKey(id, info.Kind, existing, secret);
		byte[]? passphrase = Merge(id, PassphraseField, existing?.Passphrase, secret?.Passphrase);
		byte[]? proxyPassword = Merge(id, ProxyPasswordField, existing?.ProxyPassword, secret?.ProxyPassword);
		DateTimeOffset now = _timeProvider.GetUtcNow();
		return new StoredCredential
		{
			Info = new CredentialInfo
			{
				Id = id,
				Name = name,
				Kind = info.Kind,
				Username = InputText.TrimToNull(info.Username),
				IsShared = info.IsShared,
				OwnerConnectionId = info.OwnerConnectionId,
				KeyAlgorithm = keyAlgorithm,
				KeyFingerprint = keyFingerprint,
				HasPassphrase = passphrase is not null,
				HasProxyPassword = proxyPassword is not null,
				Notes = InputText.TrimToNull(info.Notes),
				CreatedAt = existing?.Info.CreatedAt ?? now,
				UpdatedAt = now,
			},
			Password = Merge(id, PasswordField, existing?.Password, secret?.Password),
			PrivateKey = Merge(id, PrivateKeyField, existing?.PrivateKey, secret?.PrivateKey),
			Passphrase = passphrase,
			ProxyPassword = proxyPassword,
		};
	}

	/// <summary>
	/// Inspects the key that will be stored when it or its passphrase changed. A kept key keeps its recorded algorithm
	/// and fingerprint; without an inspector a new key has neither.
	/// </summary>
	private (string? Algorithm, string? Fingerprint) DescribeKey(Guid id, CredentialKind kind, StoredCredential? existing, CredentialSecretInput? secret)
	{
		if (kind != CredentialKind.PrivateKey)
		{
			return (null, null);
		}

		if (existing is { Info.Kind: CredentialKind.PrivateKey } && secret?.PrivateKey is null && (secret?.Passphrase is null || _inspector is null))
		{
			return (existing.Info.KeyAlgorithm, existing.Info.KeyFingerprint);
		}

		if (_inspector is null)
		{
			return (null, null);
		}

		string privateKey = secret?.PrivateKey is { Length: > 0 } privateKeyInput
			? privateKeyInput
			: RevealString(id, PrivateKeyField, existing?.PrivateKey) ?? "";
		string? passphrase = secret?.Passphrase is { } passphraseInput
			? (passphraseInput.Length == 0 ? null : passphraseInput)
			: RevealString(id, PassphraseField, existing?.Passphrase);

		PrivateKeyInspection inspection = _inspector.Inspect(privateKey, passphrase);
		return inspection.IsValid
			? (inspection.Algorithm, inspection.Fingerprint)
			: throw new CredentialValidationException(inspection.Status, ValidationMessage(inspection));
	}

	private static bool HasValue(byte[]? stored, string? input) => input is null ? stored is not null : input.Length > 0;

	/// <summary>Applies the editor rule for one field: null keeps the stored value, empty clears it, anything else replaces it.</summary>
	private byte[]? Merge(Guid id, string field, byte[]? stored, string? input)
	{
		if (input is null)
		{
			return stored;
		}

		if (input.Length == 0)
		{
			return null;
		}

		using PinnedBytes plaintext = PinnedBytes.FromString(input);
		return _cipher.EncryptToArray(plaintext.Span, VaultCrypto.CredentialFieldAssociatedData(id, field));
	}

	private SecretBuffer? OpenSecret(Guid id, string field, byte[]? sealedBox)
	{
		if (sealedBox is null)
		{
			return null;
		}

		using PinnedBytes plaintext = _cipher.DecryptToPinned(sealedBox, VaultCrypto.CredentialFieldAssociatedData(id, field));
		return SecretBuffer.FromBytes(plaintext.Span);
	}

	/// <summary>Only for <see cref="IPrivateKeyInspector"/>, whose API takes strings.</summary>
	private string? RevealString(Guid id, string field, byte[]? sealedBox)
	{
		using SecretBuffer? secret = OpenSecret(id, field, sealedBox);
		return secret?.RevealString();
	}

	private void OnExternalChange() => Changed?.Invoke();
}
