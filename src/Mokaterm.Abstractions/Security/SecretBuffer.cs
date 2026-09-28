using System.Security.Cryptography;
using System.Text;

namespace Mokaterm.Abstractions.Security;

/// <summary>
/// Decrypted secret bytes in a pinned array that the GC cannot copy, wiped on dispose. Keep calls to
/// <see cref="RevealString"/> at the boundary of libraries that insist on strings.
/// </summary>
public sealed class SecretBuffer : IDisposable
{
	private byte[]? _buffer;

	private SecretBuffer(byte[] pinned) => _buffer = pinned;

	public int Length => Buffer.Length;

	public bool IsDisposed => _buffer is null;

	public ReadOnlySpan<byte> Span => Buffer;

	private byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(SecretBuffer));

	public static SecretBuffer FromBytes(ReadOnlySpan<byte> bytes)
	{
		byte[] pinned = GC.AllocateUninitializedArray<byte>(bytes.Length, pinned: true);
		bytes.CopyTo(pinned);
		return new SecretBuffer(pinned);
	}

	/// <summary>UTF-8 encodes <paramref name="value"/> straight into pinned memory.</summary>
	public static SecretBuffer FromString(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		byte[] pinned = GC.AllocateUninitializedArray<byte>(Encoding.UTF8.GetByteCount(value), pinned: true);
		Encoding.UTF8.GetBytes(value, pinned);
		return new SecretBuffer(pinned);
	}

	/// <summary>Decodes the secret as UTF-8. The returned string cannot be wiped, so use it and let it go.</summary>
	public string RevealString() => Encoding.UTF8.GetString(Buffer);

	public SecretBuffer Copy() => FromBytes(Buffer);

	public void Dispose()
	{
		byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
		if (buffer is not null)
		{
			CryptographicOperations.ZeroMemory(buffer);
		}
	}
}
