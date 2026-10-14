using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Devolutions.IronRdp;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Carries the Kerberos requests CredSSP hands out. sspi-rs, which does CredSSP inside IronRDP, picks Kerberos only
/// when it finds a Kerberos server for the login's domain (DNS SRV records, the Kerberos domains in the registry, or the
/// <c>SSPI_KDC_URL</c> variables) and NTLM otherwise, so a workgroup PC or a Microsoft account never comes here.
/// When it does pick Kerberos, the server it found may still be out of reach (a laptop away from the office), and
/// IronRDP's own client then waited on the TCP connect with no limit and kept one connection for every request. Each
/// request here gets its own connection and <see cref="RequestTimeout"/>, and a server that does not answer ends the
/// attempt with a message that names it, rather than holding the connect until its deadline.
/// </summary>
internal static class RdpKerberosClient
{
	/// <summary>What one request may take, connecting included. A reachable Kerberos server answers in milliseconds.</summary>
	public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

	/// <summary>A ticket with a large group list runs to tens of kilobytes; nothing a Kerberos server sends comes near this.</summary>
	public const int MaxReplyBytes = 1024 * 1024;

	public const int DefaultPort = 88;

	/// <summary>The server a request goes to, as status lines and messages name it.</summary>
	public static string Describe(string url) =>
		TryParse(url, out string host, out int port) ? HostEndpoint.Format(host, port) : url;

	/// <summary>Sends one request and returns the reply the way sspi-rs reads it: over TCP with its length in front.</summary>
	/// <exception cref="ProtocolConnectException">The Kerberos server did not answer, or not in time.</exception>
	public static async Task<byte[]> SendAsync(NetworkRequestProtocol protocol, string url, byte[] request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!TryParse(url, out string host, out int port))
		{
			throw new ProtocolConnectException(
				ConnectFailure.ProtocolError,
				$"CredSSP asked for the Kerberos server {url}, which is not an address this client can reach.");
		}

		string endpoint = HostEndpoint.Format(host, port);
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(RequestTimeout);
		try
		{
			return protocol switch
			{
				NetworkRequestProtocol.Tcp => await SendTcpAsync(host, port, request, deadline.Token),
				NetworkRequestProtocol.Udp => await SendUdpAsync(host, port, request, deadline.Token),
				_ => throw new ProtocolConnectException(
					ConnectFailure.ProtocolError,
					$"CredSSP asked for the Kerberos server {endpoint} over {protocol}, which this client does not speak."),
			};
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new ProtocolConnectException(ConnectFailure.Timeout, NoAnswer(endpoint));
		}
		catch (Exception ex) when (ex is SocketException or IOException)
		{
			SocketException? socket = ex as SocketException ?? ex.InnerException as SocketException;
			string reason = ex is EndOfStreamException
				? "closed the connection without answering"
				: socket?.SocketErrorCode == SocketError.ConnectionRefused
					? "refused the connection"
					: socket?.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData
						? "could not be found"
						: "could not be reached";
			throw new ProtocolConnectException(ConnectFailure.HostUnreachable, Unreachable(endpoint, reason), ex);
		}
	}

	/// <summary>The sentence for a Kerberos server that never answered, which is also what a timed out Kerberos step says.</summary>
	public static string NoAnswer(string endpoint) =>
		Unreachable(endpoint, string.Create(CultureInfo.InvariantCulture, $"did not answer within {RequestTimeout.TotalSeconds:0} seconds"));

	/// <summary>The sentence for a Kerberos server at <paramref name="endpoint"/> that <paramref name="reason"/>.</summary>
	public static string Unreachable(string endpoint, string reason) =>
		$"This login's domain has a Kerberos server, {endpoint}, which {reason}. A domain login needs it to be reachable " +
		"from this computer. To log in to a local account on that PC instead, write the user name as PCNAME\\user.";

	private static bool TryParse(string url, out string host, out int port)
	{
		host = "";
		port = DefaultPort;
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Host.Length == 0)
		{
			return false;
		}

		host = uri.HostNameType == UriHostNameType.IPv6 ? uri.Host.Trim('[', ']') : uri.Host;
		port = uri.IsDefaultPort || uri.Port <= 0 ? DefaultPort : uri.Port;
		return true;
	}

	private static async Task<byte[]> SendTcpAsync(string host, int port, byte[] request, CancellationToken cancellationToken)
	{
		using TcpClient client = new();
		await client.ConnectAsync(host, port, cancellationToken);
		NetworkStream stream = client.GetStream();

		// sspi-rs puts the four byte length in front of a TCP request itself.
		await stream.WriteAsync(request, cancellationToken);
		byte[] header = new byte[4];
		await stream.ReadExactlyAsync(header, cancellationToken);
		uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
		if (length > MaxReplyBytes)
		{
			throw new IOException(string.Create(CultureInfo.InvariantCulture, $"The Kerberos server announced a {length} byte reply."));
		}

		byte[] reply = new byte[4 + (int)length];
		header.CopyTo(reply, 0);
		await stream.ReadExactlyAsync(reply.AsMemory(4), cancellationToken);
		return reply;
	}

	private static async Task<byte[]> SendUdpAsync(string host, int port, byte[] request, CancellationToken cancellationToken)
	{
		IPAddress[] addresses = IPAddress.TryParse(host, out IPAddress? literal)
			? [literal]
			: await Dns.GetHostAddressesAsync(host, cancellationToken);
		if (addresses.Length == 0)
		{
			throw new SocketException((int)SocketError.HostNotFound);
		}

		using UdpClient client = new(addresses[0].AddressFamily);
		client.Connect(new IPEndPoint(addresses[0], port));
		await client.SendAsync(request, cancellationToken);
		UdpReceiveResult result = await client.ReceiveAsync(cancellationToken);
		return result.Buffer;
	}
}
