namespace Mokaterm.Abstractions.Storage;

/// <summary>
/// Plain JSON documents in the app data directory, such as settings and UI layout. Never store secrets,
/// hostnames or usernames here; use <see cref="IVaultDataStore"/>.
/// Document names are lowercase letters, digits, dots and dashes.
/// </summary>
public interface IAppDataStore
{
	ValueTask<T?> ReadAsync<T>(string name, CancellationToken cancellationToken = default) where T : class;

	/// <summary>Writes atomically: a crash leaves either the old or the new document, never half of one.</summary>
	Task WriteAsync<T>(string name, T document, CancellationToken cancellationToken = default) where T : class;

	Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON documents encrypted with the vault's data key, for module data that must not sit on disk in plain
/// text. Every method throws <see cref="Security.VaultLockedException"/> while the vault is locked.
/// </summary>
public interface IVaultDataStore
{
	/// <summary>Raised with the document name after a write or delete, including writes from other UI scopes.</summary>
	event Action<string>? DocumentChanged;

	/// <summary>
	/// Raised when a document could not be decrypted or parsed and was set aside, so it read as missing. Every UI scope
	/// hears it, whichever one read the document, because they all lose it. Handlers may run on any thread.
	/// </summary>
	event Action<DamagedDocument>? DocumentDamaged;

	ValueTask<T?> ReadAsync<T>(string name, CancellationToken cancellationToken = default) where T : class;

	Task WriteAsync<T>(string name, T document, CancellationToken cancellationToken = default) where T : class;

	Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
