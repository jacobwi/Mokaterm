using System.Buffers;
using System.Globalization;
using FluentFTP;

namespace Mokaterm.Modules.Ftp.FileSystem;

/// <summary>Copying through and closing the streams returned by FluentFTP's OpenRead and OpenWrite.</summary>
internal static class FtpDataStreams
{
	private const int CopyBufferSize = 81920;

	/// <summary>
	/// Copies everything, reporting the running total after each chunk. With <paramref name="writeTimeout"/>, a write
	/// that makes no progress for that long fails with <see cref="TimeoutException"/>: FluentFTP puts no limit on
	/// asynchronous writes to a data connection, so a server that stops reading would hold an upload forever.
	/// </summary>
	public static async Task CopyAsync(Stream source, Stream destination, IProgress<long>? progress, TimeSpan? writeTimeout, CancellationToken cancellationToken)
	{
		byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
		using CancellationTokenSource? stall = writeTimeout is null ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		try
		{
			long total = 0;
			int read;
			while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
			{
				if (stall is not null && writeTimeout is { } timeout)
				{
					await WriteWithinAsync(destination, buffer.AsMemory(0, read), stall, timeout, cancellationToken);
				}
				else
				{
					await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				}

				total += read;
				progress?.Report(total);
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	/// <summary>
	/// Closes a finished transfer and reads the server's final reply, which is where a server reports that an upload did
	/// not fit or a download broke off. Throws <see cref="FluentFTP.Exceptions.FtpCommandException"/> for such a reply.
	/// </summary>
	public static async Task FinishAsync(Stream stream, CancellationToken cancellationToken)
	{
		if (stream is FtpDataStream data)
		{
			await data.CloseAsync(cancellationToken);
		}

		await stream.DisposeAsync();
	}

	/// <summary>Like <see cref="FinishAsync"/>, but reports failure instead of throwing.</summary>
	public static async Task<bool> FinishQuietlyAsync(Stream stream)
	{
		try
		{
			await FinishAsync(stream, CancellationToken.None);
			return true;
		}
		catch (Exception)
		{
			await DisposeQuietlyAsync(stream);
			return false;
		}
	}

	/// <summary>Ends a transfer that stopped part way and hands its connection back.</summary>
	public static async Task AbandonAsync(Stream stream, FtpClientLease lease)
	{
		if (lease.IsShared)
		{
			// The browsing connection cannot be replaced, so read the aborted transfer's reply to keep it in step.
			await FinishQuietlyAsync(stream);
			await lease.ReleaseAsync(reusable: false);
			return;
		}

		// Close the connection first: the data stream would otherwise wait for the server's reply to the aborted transfer.
		await lease.ReleaseAsync(reusable: false);
		await DisposeQuietlyAsync(stream);
	}

	private static async Task WriteWithinAsync(
		Stream destination,
		ReadOnlyMemory<byte> data,
		CancellationTokenSource stall,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		stall.CancelAfter(timeout);
		try
		{
			await destination.WriteAsync(data, stall.Token);
		}
		catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"The server took no data for {timeout.TotalSeconds:0} seconds."), ex);
		}

		stall.CancelAfter(Timeout.InfiniteTimeSpan);
	}

	public static async Task DisposeQuietlyAsync(Stream stream)
	{
		try
		{
			await stream.DisposeAsync();
		}
		catch (Exception)
		{
			// The transfer already failed or was abandoned; a second error while closing adds nothing.
		}
	}
}
