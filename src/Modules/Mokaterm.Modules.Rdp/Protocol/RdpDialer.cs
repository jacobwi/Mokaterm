using System.Net;
using System.Net.Sockets;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Opens the TCP connection. A name can resolve to several addresses, and the first is not always one that answers
/// (a stale IPv6 address, a VPN adapter's), so each gets its share of the time left and the next one is tried after it.
/// IronRDP's own dial took the first address only and waited on it with no limit.
/// </summary>
internal static class RdpDialer
{
	/// <summary>Connects to the first address of <paramref name="host"/> that answers within its share of <paramref name="budget"/>.</summary>
	/// <param name="tried">Told about each address as the dial moves on to it.</param>
	/// <exception cref="SocketException">
	/// No address took the connection: the refusal when any address refused, since that says the machine is there,
	/// otherwise the last failure, which is <see cref="SocketError.TimedOut"/> when nothing answered.
	/// </exception>
	public static async Task<Socket> ConnectAsync(
		string host,
		int port,
		TimeSpan budget,
		Action<IPAddress>? tried,
		CancellationToken cancellationToken)
	{
		IPAddress[] addresses = IPAddress.TryParse(host, out IPAddress? literal)
			? [literal]
			: await Dns.GetHostAddressesAsync(host, cancellationToken);
		if (addresses.Length == 0)
		{
			throw new SocketException((int)SocketError.HostNotFound);
		}

		DateTimeOffset end = DateTimeOffset.UtcNow + budget;
		SocketException? refused = null;
		SocketException? last = null;
		for (int i = 0; i < addresses.Length; i++)
		{
			IPAddress address = addresses[i];
			tried?.Invoke(address);
			TimeSpan left = end - DateTimeOffset.UtcNow;
			TimeSpan share = left <= TimeSpan.Zero ? TimeSpan.Zero : left / (addresses.Length - i);
			Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
			{
				// Input events are small and latency bound; Nagle would hold them back.
				NoDelay = true,
			};

			using CancellationTokenSource attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			attempt.CancelAfter(share);
			try
			{
				await socket.ConnectAsync(new IPEndPoint(address, port), attempt.Token);
				return socket;
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				socket.Dispose();
				last = new SocketException((int)SocketError.TimedOut);
			}
			catch (SocketException ex)
			{
				socket.Dispose();
				last = ex;
				if (ex.SocketErrorCode == SocketError.ConnectionRefused)
				{
					refused ??= ex;
				}
			}
			catch
			{
				socket.Dispose();
				throw;
			}
		}

		throw refused ?? last ?? new SocketException((int)SocketError.TimedOut);
	}
}
