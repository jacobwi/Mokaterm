using System.Net.Sockets;
using System.Security.Cryptography;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>
/// Relays one forwarded X11 connection between the server's program and the local X server. The program's connection
/// setup is held back until it is whole: its cookie has to be the fake one this session handed the server, and the X
/// server gets the real cookie in its place, or no authorization at all when there is no real one. A wrong cookie means
/// someone on the server who was not given this display, and the connection goes no further.
/// </summary>
internal static class X11Relay
{
	private const int ReadBytes = 32 * 1024;

	private const int FirstReadBytes = 1024;

	/// <summary>A program that opens a connection and never sends its setup holds a socket on this machine; this long at most.</summary>
	private static readonly TimeSpan SetupTimeout = TimeSpan.FromSeconds(60);

	/// <summary>Runs the connection until both sides finish or the channel ends.</summary>
	/// <param name="realCookie">The X server's own cookie, or null to send no authorization.</param>
	/// <returns>Why the connection was refused, or null when it ran its course.</returns>
	public static async Task<string?> RunAsync(SshForwardedChannel channel, Socket server, ReadOnlyMemory<byte> fakeCookie, byte[]? realCookie, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(channel);
		ArgumentNullException.ThrowIfNull(server);
		byte[]? first;
		using (CancellationTokenSource setupDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
		{
			setupDeadline.CancelAfter(SetupTimeout);
			(first, string? refusal) = await ReadSetupAsync(channel, fakeCookie, realCookie, setupDeadline.Token);
			if (first is null)
			{
				return refusal;
			}
		}

		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		using NetworkStream stream = new(server, ownsSocket: false);
		try
		{
			await stream.WriteAsync(first, stop.Token);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(first);
		}

		Task up = ToServerAsync(channel, stream, server, stop.Token);
		Task down = ToChannelAsync(stream, channel, stop.Token);
		Task both = Task.WhenAll(up, down);

		// The channel ending (the server closed it, the connection dropped) ends both directions at once.
		_ = await Task.WhenAny(both, channel.Closed);
		await stop.CancelAsync();
		await both;
		return null;
	}

	private static async Task<(byte[]? First, string? Refusal)> ReadSetupAsync(SshForwardedChannel channel, ReadOnlyMemory<byte> fakeCookie, byte[]? realCookie, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[FirstReadBytes];
		int count = 0;
		try
		{
			while (true)
			{
				if (count == buffer.Length)
				{
					// The setup is never longer than this, so a full buffer that is still incomplete is not one.
					if (buffer.Length >= X11SetupRequest.MaxLength)
					{
						return (null, "the connection setup was too long");
					}

					byte[] grown = new byte[Math.Min(buffer.Length * 4, X11SetupRequest.MaxLength)];
					buffer.AsSpan(0, count).CopyTo(grown);
					CryptographicOperations.ZeroMemory(buffer);
					buffer = grown;
				}

				int read;
				try
				{
					read = await channel.ReadAsync(buffer.AsMemory(count), cancellationToken);
				}
				catch (OperationCanceledException)
				{
					return (null, cancellationToken.IsCancellationRequested ? "no connection setup arrived in time" : null);
				}

				if (read == 0)
				{
					return (null, null);
				}

				count += read;
				switch (X11SetupRequest.TryRead(buffer.AsSpan(0, count), out X11SetupRequest request))
				{
					case X11SetupStatus.Invalid:
						return (null, "it did not start with an X11 connection setup");
					case X11SetupStatus.NeedMore:
						continue;
				}

				ReadOnlySpan<byte> setup = buffer.AsSpan(0, request.Length);
				if (!request.Carries(setup, fakeCookie.Span))
				{
					return (null, "its cookie is not the one this session handed the server");
				}

				byte[] rewritten = realCookie is { Length: > 0 } real
					? request.WithAuthorization(setup, X11SetupRequest.MitMagicCookie, real)
					: request.WithAuthorization(setup, "", []);
				byte[] first = new byte[rewritten.Length + count - request.Length];
				rewritten.CopyTo(first, 0);
				buffer.AsSpan(request.Length, count - request.Length).CopyTo(first.AsSpan(rewritten.Length));
				CryptographicOperations.ZeroMemory(rewritten);
				return (first, null);
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(buffer);
		}
	}

	private static async Task ToServerAsync(SshForwardedChannel channel, NetworkStream stream, Socket server, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[ReadBytes];
		try
		{
			while (true)
			{
				int read = await channel.ReadAsync(buffer, cancellationToken);
				if (read == 0)
				{
					// The program sent EOF: the X server sees the same, and may still answer what it already got.
					server.Shutdown(SocketShutdown.Send);
					return;
				}

				await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
			}
		}
		catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
		{
			// The X server went away or the relay was stopped; the caller closes the channel either way.
		}
	}

	private static async Task ToChannelAsync(NetworkStream stream, SshForwardedChannel channel, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[ReadBytes];
		try
		{
			while (true)
			{
				int read = await stream.ReadAsync(buffer, cancellationToken);
				if (read == 0)
				{
					channel.SendEof();
					return;
				}

				if (!await channel.WriteAsync(buffer.AsMemory(0, read), cancellationToken))
				{
					return;
				}
			}
		}
		catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
		{
			// As above: whatever ended it, the channel is closed by the caller.
		}
	}
}
