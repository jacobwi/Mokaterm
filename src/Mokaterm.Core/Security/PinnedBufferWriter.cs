using System.Buffers;
using System.Security.Cryptography;

namespace Mokaterm.Core.Security;

/// <summary>
/// A growable buffer in pinned memory for serializing documents before encryption. Outgrown buffers and the final
/// one are wiped, so the plaintext JSON does not linger on the managed heap.
/// </summary>
internal sealed class PinnedBufferWriter : IBufferWriter<byte>, IDisposable
{
	private const int MinimumGrowth = 4096;

	private byte[] _buffer;
	private int _written;

	public PinnedBufferWriter(int initialCapacity = 16 * 1024) =>
		_buffer = GC.AllocateUninitializedArray<byte>(initialCapacity, pinned: true);

	public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

	public void Advance(int count)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(count);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _buffer.Length - _written);
		_written += count;
	}

	public Memory<byte> GetMemory(int sizeHint = 0)
	{
		EnsureCapacity(sizeHint);
		return _buffer.AsMemory(_written);
	}

	public Span<byte> GetSpan(int sizeHint = 0)
	{
		EnsureCapacity(sizeHint);
		return _buffer.AsSpan(_written);
	}

	public void Dispose()
	{
		CryptographicOperations.ZeroMemory(_buffer);
		_written = 0;
	}

	private void EnsureCapacity(int sizeHint)
	{
		int needed = Math.Max(sizeHint, 1);
		if (_buffer.Length - _written >= needed)
		{
			return;
		}

		int newSize = Math.Max(_buffer.Length * 2, _written + Math.Max(needed, MinimumGrowth));
		byte[] grown = GC.AllocateUninitializedArray<byte>(newSize, pinned: true);
		_buffer.AsSpan(0, _written).CopyTo(grown);
		CryptographicOperations.ZeroMemory(_buffer);
		_buffer = grown;
	}
}
