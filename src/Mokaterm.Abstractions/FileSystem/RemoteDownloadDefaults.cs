using System.Buffers;

namespace Mokaterm.Abstractions.FileSystem;

/// <summary>What <see cref="IRemoteFileSystem.DownloadAsync(string, Stream, long, IProgress{long}?, CancellationToken)"/> does when an implementation leaves it out.</summary>
internal static class RemoteDownloadDefaults
{
	private const int CopyBufferSize = 128 * 1024;

	public static async ValueTask DownloadFromAsync(
		IRemoteFileSystem fileSystem,
		string path,
		Stream destination,
		long offset,
		IProgress<long>? progress,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(destination);
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		if (offset == 0)
		{
			await fileSystem.DownloadAsync(path, destination, progress, cancellationToken);
			return;
		}

		Stream source = await fileSystem.OpenReadAsync(path, cancellationToken);
		await using (source)
		{
			if (!await TransferStreams.TrySkipAsync(source, offset, cancellationToken))
			{
				throw TransferStreams.ShorterThanCopied(path, offset);
			}

			byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
			try
			{
				long total = 0;
				int read;
				while ((read = await source.ReadAsync(buffer.AsMemory(0, CopyBufferSize), cancellationToken)) > 0)
				{
					await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
					total += read;
					progress?.Report(total);
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}
	}
}
