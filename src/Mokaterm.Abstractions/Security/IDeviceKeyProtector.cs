namespace Mokaterm.Abstractions.Security;

/// <summary>
/// Platform key protection used for "unlock on this device": DPAPI on Windows, the Keychain on macOS.
/// Hosts that cannot protect a key per user (the web host) do not register one.
/// </summary>
public interface IDeviceKeyProtector
{
	/// <summary>Shown next to the setting, for example "Windows account (DPAPI)".</summary>
	string Description { get; }

	ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

	/// <exception cref="System.Security.Cryptography.CryptographicException">The data was protected for a different user or machine.</exception>
	ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedData, CancellationToken cancellationToken = default);
}
