using System.Buffers;

namespace Mokaterm.Sessions.Terminal;

/// <summary>A pooled copy of terminal output. Whoever holds it last returns it to the pool, exactly once.</summary>
internal readonly record struct OutputChunk(byte[] Buffer, int Length)
{
	public ReadOnlyMemory<byte> Memory => Buffer.AsMemory(0, Length);

	public static OutputChunk Rent(int length) => new(ArrayPool<byte>.Shared.Rent(length), length);

	public static OutputChunk Copy(ReadOnlySpan<byte> data)
	{
		OutputChunk chunk = Rent(data.Length);
		data.CopyTo(chunk.Buffer);
		return chunk;
	}

	public void Return() => ArrayPool<byte>.Shared.Return(Buffer);
}
