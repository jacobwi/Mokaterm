using Mokaterm.Abstractions.Credentials;
using Mokaterm.Core.Keys;

namespace Mokaterm.Core.Import;

/// <summary>
/// The keychain side of one import: each key file is read once, and every login that names the same file, a file with
/// the same public key, or a key the keychain already holds ends up on one shared keychain entry. A key kept with its
/// passphrase still on is saved locked, so the login asks for the passphrase when it connects, the way it does for any
/// saved key whose passphrase is not stored.
/// </summary>
internal sealed class KeychainImport
{
	/// <summary>Paths compare the way this machine's file system does.</summary>
	public static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

	private static readonly string[] KeyExtensions = [".ppk", ".pem"];

	private readonly ICredentialStore _store;
	private readonly IdentityFileReader _reader;
	private readonly Dictionary<string, Guid> _byFingerprint;
	private readonly Dictionary<string, (Guid? Id, string? Problem)> _byPath = new(PathComparer);

	private KeychainImport(ICredentialStore store, IdentityFileReader reader, Dictionary<string, Guid> byFingerprint)
	{
		_store = store;
		_reader = reader;
		_byFingerprint = byFingerprint;
	}

	/// <summary>New keychain entries this import made.</summary>
	public int Added { get; private set; }

	/// <exception cref="Abstractions.Security.VaultLockedException">The vault is locked.</exception>
	public static async Task<KeychainImport> StartAsync(ICredentialStore store, IdentityFileReader reader, CancellationToken cancellationToken)
	{
		IReadOnlyList<CredentialInfo> keychain = await store.ListAsync(cancellationToken);
		Dictionary<string, Guid> byFingerprint = new(StringComparer.Ordinal);
		foreach ((string fingerprint, CredentialInfo credential) in SharedKeys(keychain))
		{
			byFingerprint[fingerprint] = credential.Id;
		}

		return new KeychainImport(store, reader, byFingerprint);
	}

	/// <summary>
	/// The keychain's shared private keys by fingerprint. A login's private credential is deleted with that login, so no
	/// other login may come to depend on it.
	/// </summary>
	public static Dictionary<string, CredentialInfo> SharedKeys(IEnumerable<CredentialInfo> credentials)
	{
		Dictionary<string, CredentialInfo> keys = new(StringComparer.Ordinal);
		foreach (CredentialInfo credential in credentials)
		{
			if (credential is { Kind: CredentialKind.PrivateKey, IsShared: true, KeyFingerprint: { Length: > 0 } fingerprint })
			{
				keys.TryAdd(fingerprint, credential);
			}
		}

		return keys;
	}

	/// <summary>
	/// The keychain entry for the key at <paramref name="path"/>, added on first use. Either the id or a sentence saying
	/// why the key could not be added comes back.
	/// </summary>
	public async Task<(Guid? Id, string? Problem)> CredentialForAsync(string path, CancellationToken cancellationToken)
	{
		if (_byPath.TryGetValue(path, out (Guid? Id, string? Problem) known))
		{
			return known;
		}

		(Guid? Id, string? Problem) outcome = await AddAsync(path, cancellationToken);
		_byPath[path] = outcome;
		return outcome;
	}

	/// <summary>The file's name, without the extension PuTTY and PEM keys usually carry: <c>id_ed25519</c>, <c>work</c>.</summary>
	internal static string KeyName(string path)
	{
		string name = Path.GetFileName(path);
		string? extension = KeyExtensions.FirstOrDefault(candidate => name.EndsWith(candidate, StringComparison.OrdinalIgnoreCase));
		return extension is not null && name.Length > extension.Length ? name[..^extension.Length] : name;
	}

	private async Task<(Guid? Id, string? Problem)> AddAsync(string path, CancellationToken cancellationToken)
	{
		// Read again rather than carried over from the preview, so no key sits in the wizard's state while the user looks.
		using KeyFileMaterial? material = await Task.Run(() => _reader.Read(path), cancellationToken);
		if (material is null)
		{
			return (null, $"There is no key file at {path} any more.");
		}

		if (material is not { Status: Abstractions.Import.ImportedKeyStatus.Ready, KeyText: { } keyText })
		{
			return (null, material.Reason ?? "The key file could not be used.");
		}

		if (material.Fingerprint is { } fingerprint && _byFingerprint.TryGetValue(fingerprint, out Guid existing))
		{
			return (existing, null);
		}

		CredentialInfo info = new()
		{
			Id = Guid.NewGuid(),
			Name = KeyName(path),
			Kind = CredentialKind.PrivateKey,
			IsShared = true,
			Notes = $"Imported from {path}.",
		};

		CredentialInfo saved;
		try
		{
			saved = await _store.SaveAsync(
				info,
				new CredentialSecretInput { PrivateKey = keyText.RevealString(), AskPassphraseWhenUsed = material.NeedsPassphrase },
				cancellationToken);
		}
		catch (CredentialValidationException ex)
		{
			return (null, $"The keychain did not take the key: {ex.Message}");
		}

		Added++;
		foreach (string? known in new[] { material.Fingerprint, saved.KeyFingerprint })
		{
			if (known is { Length: > 0 })
			{
				_byFingerprint.TryAdd(known, saved.Id);
			}
		}

		return (saved.Id, null);
	}
}
