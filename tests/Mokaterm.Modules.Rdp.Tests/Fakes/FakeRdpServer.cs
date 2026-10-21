using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Mokaterm.Modules.Rdp.Tests.Fakes;

/// <summary>
/// A loopback server that plays the first steps of an RDP server and then stops at the one a test is about: it answers
/// the X.224 request with a chosen protocol, runs the TLS handshake with a self-signed certificate, and after that
/// holds the connection, hangs up, reads one message and hangs up, refuses an NTLM login, or plays the MCS steps up to
/// the Client Info PDU and reads that. Nothing past those is RDP, which is enough to see which step a client reports,
/// what it sent and that it closes what it opened.
/// </summary>
internal sealed class FakeRdpServer : IAsyncDisposable
{
	/// <summary>RDP_NEG_RSP's selectedProtocol for Standard RDP Security.</summary>
	public const int ProtocolRdp = 0;

	/// <summary>RDP_NEG_RSP's selectedProtocol for TLS alone.</summary>
	public const int ProtocolSsl = 1;

	/// <summary>RDP_NEG_RSP's selectedProtocol for CredSSP.</summary>
	public const int ProtocolHybrid = 2;

	private readonly TcpListener _listener;
	private readonly CancellationTokenSource _stopping = new();
	private readonly X509Certificate2 _certificate = TestCertificates.CreateServerCertificate("rdp.test");
	private readonly ConcurrentQueue<Task> _connections = new();
	private readonly TaskCompletionSource _clientClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private Task? _accepting;
	private int _accepted;
	private int _tlsHandshakes;
	private long _bytesAfterTls;

	public FakeRdpServer(int? selectedProtocol, bool startTls = true)
	{
		SelectedProtocol = selectedProtocol;
		StartTls = startTls;
		_listener = new TcpListener(IPAddress.Loopback, 0);
	}

	/// <summary>The protocol the X.224 answer selects, or null to answer nothing at all.</summary>
	public int? SelectedProtocol { get; }

	/// <summary>False leaves the client's TLS hello unanswered.</summary>
	public bool StartTls { get; }

	/// <summary>What each connection does once TLS is up, by its number from 1.</summary>
	public Func<int, AfterTls> Then { get; init; } = static _ => AfterTls.Hold;

	/// <summary>The X.224 answer to send instead of an RDP_NEG_RSP for <see cref="SelectedProtocol"/>.</summary>
	public byte[]? Confirm { get; init; }

	/// <summary>
	/// What happened on the server's side, in order: "tls" once a handshake finished and "client info" once a Client Info
	/// PDU was read. A test can hand in a queue its certificate check writes to as well.
	/// </summary>
	public ConcurrentQueue<string> Events { get; init; } = new();

	public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

	public int Accepted => Volatile.Read(ref _accepted);

	/// <summary>TLS handshakes that finished.</summary>
	public int TlsHandshakes => Volatile.Read(ref _tlsHandshakes);

	/// <summary>RDP_NEG_REQ's requestedProtocols from each X.224 Connection Request.</summary>
	public ConcurrentQueue<uint> RequestedProtocols { get; } = new();

	/// <summary>The domain and user each <see cref="AfterTls.RefuseLogin"/> connection received, as NTLM carried them.</summary>
	public ConcurrentQueue<(string Domain, string User)> Logins { get; } = new();

	/// <summary>What each <see cref="AfterTls.PlaySession"/> connection read in the Client Info PDU.</summary>
	public ConcurrentQueue<FakeClientInfo> ClientInfos { get; } = new();

	/// <summary>Bytes the clients sent over TLS, which is where CredSSP would carry a login.</summary>
	public long BytesAfterTls => Interlocked.Read(ref _bytesAfterTls);

	/// <summary>Completes when a client closes a connection the server was still holding open.</summary>
	public Task ClientClosed => _clientClosed.Task;

	public FakeRdpServer Start()
	{
		_listener.Start();
		_accepting = AcceptAsync();
		return this;
	}

	public async ValueTask DisposeAsync()
	{
		await _stopping.CancelAsync();
		_listener.Stop();
		if (_accepting is not null)
		{
			await _accepting.WaitAsync(TimeSpan.FromSeconds(10));
		}

		await Task.WhenAll(_connections).WaitAsync(TimeSpan.FromSeconds(10));
		_certificate.Dispose();
		_stopping.Dispose();
	}

	/// <summary>The X.224 connection confirm with an RDP_NEG_RSP that selects <paramref name="protocol"/>.</summary>
	public static byte[] ConnectionConfirm(int protocol) =>
	[
		0x03, 0x00, 0x00, 0x13,
		0x0E, 0xD0, 0x00, 0x00, 0x12, 0x34, 0x00,
		0x02, 0x00, 0x08, 0x00, (byte)protocol, 0x00, 0x00, 0x00,
	];

	/// <summary>The X.224 connection confirm with an RDP_NEG_FAILURE carrying <paramref name="code"/>.</summary>
	public static byte[] NegotiationFailure(int code) =>
	[
		0x03, 0x00, 0x00, 0x13,
		0x0E, 0xD0, 0x00, 0x00, 0x12, 0x34, 0x00,
		0x03, 0x00, 0x08, 0x00, (byte)code, 0x00, 0x00, 0x00,
	];

	/// <summary>The X.224 connection confirm of a server from before RDP 5.2, with no negotiation in it at all.</summary>
	public static byte[] ConfirmWithoutNegotiation() =>
	[
		0x03, 0x00, 0x00, 0x0B,
		0x06, 0xD0, 0x00, 0x00, 0x12, 0x34, 0x00,
	];

	private async Task AcceptAsync()
	{
		try
		{
			while (!_stopping.IsCancellationRequested)
			{
				TcpClient client = await _listener.AcceptTcpClientAsync(_stopping.Token);
				int number = Interlocked.Increment(ref _accepted);
				_connections.Enqueue(ServeAsync(client, number));
			}
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
		{
			// Stopped.
		}
	}

	private async Task ServeAsync(TcpClient client, int number)
	{
		using TcpClient owned = client;
		CancellationToken stopping = _stopping.Token;
		try
		{
			NetworkStream network = client.GetStream();
			byte[] request = await ReadTpktAsync(network, stopping);

			// RDP_NEG_REQ is the last eight bytes of the request, after the routing cookie.
			if (request.Length >= 8 + 11 && request[^8] == 0x01)
			{
				RequestedProtocols.Enqueue(BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(request.Length - 4)));
			}

			byte[]? confirm = Confirm ?? (SelectedProtocol is int protocol ? ConnectionConfirm(protocol) : null);
			if (confirm is null)
			{
				await HoldAsync(network, stopping);
				return;
			}

			await network.WriteAsync(confirm, stopping);
			bool refused = confirm.Length > 11 && confirm[11] == 0x03;
			if (!StartTls || refused)
			{
				await HoldAsync(network, stopping);
				return;
			}

			await using SslStream tls = new(network, leaveInnerStreamOpen: true);
			await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate }, stopping);
			Interlocked.Increment(ref _tlsHandshakes);
			Events.Enqueue("tls");
			switch (Then(number))
			{
				case AfterTls.HangUp:
					return;
				case AfterTls.RefuseLogin:
					Logins.Enqueue(await FakeNtlmLogin.RefuseAsync(tls, stopping));
					await HoldAsync(tls, stopping);
					return;
				case AfterTls.PlaySession:
					await PlaySessionAsync(tls, stopping);
					return;
				case AfterTls.ReadOnceThenHangUp:
					byte[] buffer = new byte[16 * 1024];
					int read = await tls.ReadAsync(buffer, stopping);
					Interlocked.Add(ref _bytesAfterTls, read);
					return;
				default:
					await HoldAsync(tls, stopping);
					return;
			}
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or IOException)
		{
			// The test is over, or the client went away mid handshake.
			if (!stopping.IsCancellationRequested)
			{
				_clientClosed.TrySetResult();
			}
		}
	}

	/// <summary>Reads whatever the client sends until it closes the connection, which is what the tests wait for.</summary>
	private async Task HoldAsync(Stream stream, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[16 * 1024];
		bool afterTls = stream is SslStream;
		while (true)
		{
			int read = await stream.ReadAsync(buffer, cancellationToken);
			if (read == 0)
			{
				_clientClosed.TrySetResult();
				return;
			}

			if (afterTls)
			{
				Interlocked.Add(ref _bytesAfterTls, read);
			}
		}
	}

	/// <summary>
	/// Answers the MCS steps of a TLS connection the way Windows does, up to the Client Info PDU: the Connect Response,
	/// then the Attach User Confirm after the client's Erect Domain and Attach User requests. Channel joins are skipped,
	/// which the Connect Response offers. It reads the Client Info PDU and hangs up.
	/// </summary>
	private async Task PlaySessionAsync(SslStream tls, CancellationToken cancellationToken)
	{
		await ReadTpktAsync(tls, cancellationToken);
		await tls.WriteAsync(RdpPdus.ServerConnectResponse(), cancellationToken);
		await ReadTpktAsync(tls, cancellationToken);
		await ReadTpktAsync(tls, cancellationToken);
		await tls.WriteAsync(RdpPdus.AttachUserConfirm(), cancellationToken);
		byte[] info = await ReadTpktAsync(tls, cancellationToken);
		ClientInfos.Enqueue(FakeClientInfo.Read(info));
		Events.Enqueue("client info");
	}

	/// <summary>Reads one whole TPKT message, header included.</summary>
	private static async Task<byte[]> ReadTpktAsync(Stream stream, CancellationToken cancellationToken)
	{
		byte[] header = new byte[4];
		await stream.ReadExactlyAsync(header, cancellationToken);
		int length = Math.Max(4, (header[2] << 8) | header[3]);
		byte[] message = new byte[length];
		header.CopyTo(message, 0);
		await stream.ReadExactlyAsync(message.AsMemory(4), cancellationToken);
		return message;
	}

	public enum AfterTls
	{
		/// <summary>Reads and answers nothing until the client goes.</summary>
		Hold,

		/// <summary>Closes the connection as soon as TLS is up.</summary>
		HangUp,

		/// <summary>Reads the client's first message and closes.</summary>
		ReadOnceThenHangUp,

		/// <summary>Runs NTLM up to the login and turns it down with STATUS_LOGON_FAILURE, as Windows does for a wrong password.</summary>
		RefuseLogin,

		/// <summary>Plays the MCS steps up to the Client Info PDU, reads it into <see cref="ClientInfos"/> and closes.</summary>
		PlaySession,
	}
}
