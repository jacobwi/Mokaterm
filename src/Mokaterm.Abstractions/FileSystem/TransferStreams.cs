using System.Buffers;
using System.Globalization;

namespace Mokaterm.Abstractions.FileSystem;

/// <summary>Stream handling every side of a resumed transfer needs: getting past what an earlier attempt already moved.</summary>
public static class TransferStreams
{
	private const int SkipBufferSize = 128 * 1024;

	/// <summary>The failure for a remote file that no longer holds the bytes an earlier attempt already copied out of it.</summary>
	public static RemoteFileSystemException ShorterThanCopied(string path, long count) => new(
		RemoteFileErrorKind.Unknown,
		string.Create(CultureInfo.CurrentCulture, $"{path} is shorter than the {count:N0} bytes already copied, so it changed since the last attempt."),
		path);

	/// <summary>
	/// Moves <paramref name="source"/> past its first <paramref name="count"/> bytes, by seeking when it can and by reading
	/// them otherwise, so a stream that hashes what passes through it still sees them. False when the stream ends first,
	/// which means it is no longer the file the earlier attempt copied.
	/// </summary>
	public static async ValueTask<bool> TrySkipAsync(Stream source, long count, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentOutOfRangeException.ThrowIfNegative(count);
		if (count == 0)
		{
			return true;
		}

		if (source.CanSeek)
		{
			if (source.Length - source.Position < count)
			{
				return false;
			}

			source.Seek(count, SeekOrigin.Current);
			return true;
		}

		byte[] buffer = ArrayPool<byte>.Shared.Rent(SkipBufferSize);
		try
		{
			long left = count;
			while (left > 0)
			{
				int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(SkipBufferSize, left)), cancellationToken);
				if (read == 0)
				{
					return false;
				}

				left -= read;
			}

			return true;
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}
}
