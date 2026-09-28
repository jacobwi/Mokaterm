using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// Protects the vault key with DPAPI for the current Windows user, so "unlock on this device" is as strong as the
/// Windows login. Another account, or a copy of the files on another machine, cannot unprotect it.
/// </summary>
internal sealed class DpapiDeviceKeyProtector : IDeviceKeyProtector
{
	// Binds the blob to this purpose so a DPAPI blob made for something else cannot be swapped in.
	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("mokaterm:device-key:v1");

	public string Description => "Windows account (DPAPI)";

	// The span overload reads the vault's pinned key where it lies, so no copy of it is left on the heap to be wiped.
	public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(ProtectedData.Protect(data.Span, DataProtectionScope.CurrentUser, Entropy));

	/// <remarks>
	/// A blob that is corrupt or belongs to another Windows user throws <see cref="CryptographicException"/>; the vault
	/// treats any failure here as "this device cannot unlock" and asks for the master password.
	/// </remarks>
	public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedData, CancellationToken cancellationToken = default)
	{
		// The key lands straight in pinned memory, so the garbage collector cannot leave a stray copy by moving the array.
		// A DPAPI blob is always longer than what it protects, so a buffer of the blob's size is big enough.
		byte[] buffer = GC.AllocateArray<byte>(protectedData.Length, pinned: true);
		try
		{
			int written = ProtectedData.Unprotect(protectedData.Span, DataProtectionScope.CurrentUser, buffer, Entropy);
			byte[] key = GC.AllocateArray<byte>(written, pinned: true);
			buffer.AsSpan(0, written).CopyTo(key);
			return ValueTask.FromResult(key);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(buffer);
		}
	}
}
