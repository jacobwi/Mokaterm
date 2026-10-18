using Mokaterm.Abstractions.Import;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Core.Keys;

/// <summary>
/// A key file as the import read it: the text the keychain stores, in pinned memory that is wiped on dispose, or the
/// reason there is none. <see cref="Status"/> is only ever <see cref="ImportedKeyStatus.Ready"/>,
/// <see cref="ImportedKeyStatus.Unreadable"/> or <see cref="ImportedKeyStatus.Unsupported"/>.
/// </summary>
internal sealed class KeyFileMaterial : IDisposable
{
	private KeyFileMaterial(ImportedKeyStatus status, string? reason) => (Status, Reason) = (status, reason);

	public ImportedKeyStatus Status { get; }

	public string? Reason { get; }

	/// <summary>The key as the keychain will keep it: the file itself, or the OpenSSH form it was converted to.</summary>
	public SecretBuffer? KeyText { get; private init; }

	public string? Algorithm { get; private init; }

	/// <summary>Null only for a PEM key whose passphrase hides its public half.</summary>
	public string? Fingerprint { get; private init; }

	public bool NeedsPassphrase { get; private init; }

	public static KeyFileMaterial Usable(SecretBuffer keyText, string? algorithm, string? fingerprint, bool needsPassphrase) =>
		new(ImportedKeyStatus.Ready, null)
		{
			KeyText = keyText,
			Algorithm = algorithm,
			Fingerprint = fingerprint,
			NeedsPassphrase = needsPassphrase,
		};

	public static KeyFileMaterial Unreadable(string reason) => new(ImportedKeyStatus.Unreadable, reason);

	public static KeyFileMaterial Unsupported(string reason) => new(ImportedKeyStatus.Unsupported, reason);

	public void Dispose() => KeyText?.Dispose();
}
