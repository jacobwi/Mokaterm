using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Devolutions.IronRdp;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>
/// The Kerberos requests CredSSP hands out. IronRDP's own client waited on the connect with no limit and read one
/// buffer of a reply, so a domain controller out of reach held the whole connect until its deadline.
/// </summary>
public sealed class RdpKerberosClientTests
{
	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task SendAsync_OverTcp_ReturnsTheWholeReplyWithItsLength()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		byte[] body = new byte[20_000];
		new Random(3).NextBytes(body);
		Task<byte[]> server = AnswerOnceAsync(listener, body, cancellationToken);
		byte[] request = Framed([1, 2, 3]);

		byte[] reply = await RdpKerberosClient.SendAsync(NetworkRequestProtocol.Tcp, Url("tcp", listener), request, cancellationToken);

		Assert.Equal(Framed(body), reply);
		Assert.Equal(request, await server);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task SendAsync_AServerThatNeverAnswers_GivesUpAndNamesIt()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		Task<TcpClient> accepted = listener.AcceptTcpClientAsync(cancellationToken).AsTask();

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => RdpKerberosClient.SendAsync(NetworkRequestProtocol.Tcp, Url("tcp", listener), Framed([1]), cancellationToken));

		Assert.Equal(ConnectFailure.Timeout, error.Failure);
		Assert.Contains($"127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}", error.Message, StringComparison.Ordinal);
		Assert.Contains("did not answer within 5 seconds", error.Message, StringComparison.Ordinal);
		(await accepted).Dispose();
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task SendAsync_AClosedPort_SaysItRefused()
	{
		int port = LoopbackPort.Free();

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => RdpKerberosClient.SendAsync(NetworkRequestProtocol.Tcp, $"tcp://127.0.0.1:{port}", Framed([1]), TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.HostUnreachable, error.Failure);
		Assert.Contains("refused the connection", error.Message, StringComparison.Ordinal);
		Assert.Contains("PCNAME\\user", error.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task SendAsync_OverUdp_ReturnsTheDatagram()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using UdpClient server = new(new IPEndPoint(IPAddress.Loopback, 0));
		int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
		Task echo = Task.Run(
			async () =>
			{
				UdpReceiveResult received = await server.ReceiveAsync(cancellationToken);
				byte[] answer = [.. received.Buffer, 9];
				await server.SendAsync(answer.AsMemory(), received.RemoteEndPoint, cancellationToken);
			},
			cancellationToken);

		byte[] reply = await RdpKerberosClient.SendAsync(NetworkRequestProtocol.Udp, $"udp://127.0.0.1:{port}", [4, 5], cancellationToken);

		Assert.Equal([4, 5, 9], reply);
		await echo;
	}

	[Fact]
	public void Describe_NamesTheServerAndThePortKerberosUses()
	{
		Assert.Equal("dc01.corp.example:88", RdpKerberosClient.Describe("tcp://dc01.corp.example"));
		Assert.Equal("dc01.corp.example:88", RdpKerberosClient.Describe("udp://dc01.corp.example:88"));
		Assert.Equal("[2001:db8::1]:750", RdpKerberosClient.Describe("tcp://[2001:db8::1]:750"));
	}

	private static string Url(string scheme, TcpListener listener) =>
		$"{scheme}://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

	private static byte[] Framed(byte[] body)
	{
		byte[] framed = new byte[4 + body.Length];
		BinaryPrimitives.WriteInt32BigEndian(framed, body.Length);
		body.CopyTo(framed, 4);
		return framed;
	}

	/// <summary>Reads one framed request and answers it in two writes, so the reply arrives in more than one read.</summary>
	private static async Task<byte[]> AnswerOnceAsync(TcpListener listener, byte[] body, CancellationToken cancellationToken)
	{
		using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
		NetworkStream stream = client.GetStream();
		byte[] header = new byte[4];
		await stream.ReadExactlyAsync(header, cancellationToken);
		byte[] request = new byte[BinaryPrimitives.ReadInt32BigEndian(header)];
		await stream.ReadExactlyAsync(request, cancellationToken);

		byte[] reply = Framed(body);
		await stream.WriteAsync(reply.AsMemory(0, 1000), cancellationToken);
		await stream.FlushAsync(cancellationToken);
		await Task.Delay(50, cancellationToken);
		await stream.WriteAsync(reply.AsMemory(1000), cancellationToken);
		return [.. header, .. request];
	}
}
