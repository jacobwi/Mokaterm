using System.Buffers;
using System.Globalization;
using Mokaterm.Abstractions.Commands;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// Reads one stream of a command to its end, keeping its first bytes and counting the rest. SSH.NET buffers whatever
/// arrives without limit when nobody reads, so a stream past the cap is still read and dropped: that is what keeps the
/// memory flat and lets the exit status arrive.
/// </summary>
internal static class OutputCapture
{
	/// <summary>Reads <paramref name="source"/> to its end.</summary>
	/// <param name="keepBytes">How much of the start is kept.</param>
	/// <param name="maxBytes">How much is read at all. Past it the stream is refused, which stops the command.</param>
	/// <param name="streamName"><c>stdout</c> or <c>stderr</c>, for the message.</param>
	/// <param name="listener">
	/// Hears every piece as it is read, as <paramref name="stream"/>, before the cap decides what is kept, so a view can
	/// show the whole stream while the capture keeps only its start. Called on this loop, which never waits for it.
	/// </param>
	/// <exception cref="RemoteCommandException">The stream held more than <paramref name="maxBytes"/>.</exception>
	public static async Task<CapturedOutput> ReadAsync(
		Stream source,
		int keepBytes,
		long maxBytes,
		string streamName,
		CancellationToken cancellationToken,
		CommandOutputHandler? listener = null,
		CommandStream stream = CommandStream.Output)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentOutOfRangeException.ThrowIfNegative(keepBytes);
		using MemoryStream kept = new(Math.Min(keepBytes, RemoteCommand.CopyBufferBytes));
		byte[] buffer = ArrayPool<byte>.Shared.Rent(RemoteCommand.CopyBufferBytes);
		try
		{
			long total = 0;
			int read;
			while ((read = await source.ReadAsync(buffer.AsMemory(0, RemoteCommand.CopyBufferBytes), cancellationToken)) > 0)
			{
				total += read;
				if (total > maxBytes)
				{
					throw new RemoteCommandException(string.Create(
						CultureInfo.CurrentCulture,
						$"The command wrote more than {maxBytes / (1024 * 1024)} MB to {streamName}, so it was stopped."));
				}

				listener?.Invoke(stream, buffer.AsSpan(0, read));
				int room = keepBytes - (int)kept.Length;
				if (room > 0)
				{
					await kept.WriteAsync(buffer.AsMemory(0, Math.Min(room, read)), cancellationToken);
				}
			}

			return new CapturedOutput(kept.ToArray(), total);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}
}
