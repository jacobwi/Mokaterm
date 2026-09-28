using System.Security.Cryptography;
using System.Text;

namespace Mokaterm.Core.Security;

/// <summary>
/// A pinned byte array for keys and decrypted material. The GC cannot copy it around, and disposing wipes it.
/// Unlike <see cref="Abstractions.Security.SecretBuffer"/> the bytes are writable, so crypto can fill them in place.
/// </summary>
internal sealed class PinnedBytes : IDisposable
{
	private byte[]? _buffer;

	public PinnedBytes(int length) => _buffer = GC.AllocateArray<byte>(length, pinned: true);

	public int Length => Buffer.Length;

	public Span<byte> Span => Buffer;

	/// <summary>The underlying array, for APIs that only take arrays. Do not keep references to it.</summary>
	public byte[] Array => Buffer;

	private byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(PinnedBytes));

	public static PinnedBytes Copy(ReadOnlySpan<byte> source)
	{
		PinnedBytes copy = new(source.Length);
		source.CopyTo(copy.Span);
		return copy;
	}

	/// <summary>UTF-8 encodes <paramref name="value"/> straight into pinned memory.</summary>
	public static PinnedBytes FromString(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		PinnedBytes bytes = new(Encoding.UTF8.GetByteCount(value));
		Encoding.UTF8.GetBytes(value, bytes.Span);
		return bytes;
	}

	public void Dispose()
	{
		byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
		if (buffer is not null)
		{
			CryptographicOperations.ZeroMemory(buffer);
		}
	}
}
