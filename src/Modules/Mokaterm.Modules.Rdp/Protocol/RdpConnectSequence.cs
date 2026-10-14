using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Devolutions.IronRdp;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// One attempt at an RDP connection, from the TCP dial to a connected session. IronRDP's <c>Connection.Connect</c>
/// does all of it in one call that takes no cancellation token, never closes its socket when it fails, waits on its
/// first DNS answer and on any Kerberos server without a limit, and writes through a thread-affine mutex, so this runs
/// the same steps itself over IronRDP's public connector types: each step follows the attempt's deadline, names
/// itself through <see cref="RdpConnectProgress"/> and leaves nothing open when it fails.
/// </summary>
/// <remarks>
/// The steps follow <c>Connection</c> and <c>ConnectionHelpers</c> in IronRDP's .NET bindings
/// (https://github.com/Devolutions/IronRDP, <c>ffi/dotnet</c>, MIT or Apache-2.0) at commit 632ad86f, the one
/// Devolutions.IronRdp 2025.12.4 was built from: X.224 negotiation until the connector asks for TLS, the TLS upgrade,
/// CredSSP with the server's public key when the server chose it, then connector steps until it is done. What differs:
/// the certificate is checked before CredSSP sends the login rather than after the whole connect, and the Kerberos
/// requests go through <see cref="RdpKerberosClient"/>. Recheck this class against those files after any update of
/// the package.
/// </remarks>
internal sealed class RdpConnectSequence : IAsyncDisposable
{
	/// <summary>A verifier that took this long asked the user, who may have left the server waiting long enough to give up.</summary>
	public static readonly TimeSpan PromptThreshold = TimeSpan.FromSeconds(2);

	private readonly RdpConnectProgress _progress;
	private readonly IHostIdentityVerifier _verifier;
	private readonly RdpDial _dial;
	private Socket? _socket;
	private NetworkStream? _network;
	private SslStream? _tls;
	private RdpFrameReader? _reader;
	private ClientConnector? _connector;
	private WriteBuf? _buffer;
	private CredsspSequenceInitResult? _credsspInit;
	private CredsspSequence? _credssp;
	private CredsspProcessGenerator? _generator;
	private Task<GeneratorState>? _abandoned;
	private int _disposed;

	/// <param name="verifier">Decides about the certificate; the caller's pauses the attempt's deadline while it asks.</param>
	/// <param name="dial">Opens the TCP connection, <see cref="RdpDialer.ConnectAsync"/> unless a test says otherwise.</param>
	public RdpConnectSequence(RdpConnectProgress progress, IHostIdentityVerifier verifier, RdpDial? dial = null)
	{
		ArgumentNullException.ThrowIfNull(progress);
		ArgumentNullException.ThrowIfNull(verifier);
		_progress = progress;
		_verifier = verifier;
		_dial = dial ?? RdpDialer.ConnectAsync;
	}

	/// <summary>True once the certificate check took long enough that the user was asked.</summary>
	public bool CertificatePrompted { get; private set; }

	/// <summary>CredSSP messages sent so far. The second one carries the answer to the server's NTLM challenge.</summary>
	public int LoginMessagesSent { get; private set; }

	/// <summary>Runs the attempt. Everything it opened is closed by <see cref="DisposeAsync"/> unless it went into the transport.</summary>
	/// <param name="config">Read once, when the connector is built; the caller keeps it.</param>
	/// <param name="dialBudget">How long the dial may take in all, shared out among the addresses the name resolves to.</param>
	public async Task<RdpTransport> RunAsync(Config config, TimeSpan dialBudget, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(config);
		string host = _progress.Host;
		bool literal = IPAddress.TryParse(host, out _);

		_progress.Enter(RdpConnectStage.Dialing);
		_socket = await _dial(
			host,
			_progress.Port,
			dialBudget,
			literal ? null : address => _progress.Enter(RdpConnectStage.Dialing, address.ToString()),
			cancellationToken);
		_network = new NetworkStream(_socket, ownsSocket: true);

		_progress.Enter(RdpConnectStage.Negotiating);
		_connector = ClientConnector.New(config, ClientAddress(_socket));

		// What ConnectionHelpers.SetupConnector does without a clipboard: the display control channel carries resizes.
		_connector.WithDynamicChannelDisplayControl();
		_buffer = WriteBuf.New();
		_reader = new RdpFrameReader(_network);
		while (!_connector.ShouldPerformSecurityUpgrade())
		{
			await StepAsync(_network, cancellationToken);
		}

		if (_reader.Buffered > 0)
		{
			throw new ProtocolConnectException(
				ConnectFailure.ProtocolError,
				$"{_progress.Endpoint} sent more than its RDP answer before the TLS handshake, which an RDP server never does.");
		}

		_progress.Enter(RdpConnectStage.Tls);
		_tls = new SslStream(_network, leaveInnerStreamOpen: false);
		await _tls.AuthenticateAsClientAsync(TlsOptions(host, literal), cancellationToken);
		_reader.Dispose();
		_reader = new RdpFrameReader(_tls);
		_connector.MarkSecurityUpgradeAsDone();

		using X509Certificate2? certificate = TlsHostIdentity.Read(_tls);
		(HostIdentity? identity, bool verified) = await CheckCertificateAsync(certificate, cancellationToken);

		bool credssp = _connector.ShouldPerformCredssp();
		if (credssp)
		{
			_progress.Enter(RdpConnectStage.Login);
			byte[] publicKey = certificate?.GetPublicKey()
				?? throw new ProtocolConnectException(
					ConnectFailure.ProtocolError,
					$"{_progress.Endpoint} chose CredSSP but presented no certificate, and CredSSP is bound to one.");
			await RunCredsspAsync(publicKey, cancellationToken);
		}

		_progress.Enter(RdpConnectStage.Session);
		while (true)
		{
			// A DynState is a view of the connector's state, so it goes before the next step changes that state.
			using (DynState state = _connector.GetDynState())
			{
				if (state.IsTerminal())
				{
					break;
				}

				_progress.Note(state.GetName());
			}

			await StepAsync(_tls, cancellationToken);
		}

		return TakeTransport(credssp, identity, verified);
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		// The connection goes first: a read or a write still waiting on it fails at once.
		try
		{
			if (_tls is not null)
			{
				await _tls.DisposeAsync();
			}
			else if (_network is not null)
			{
				await _network.DisposeAsync();
			}
			else
			{
				_socket?.Dispose();
			}
		}
		catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
		{
			// Closed either way.
		}

		_reader?.Dispose();
		_reader = null;

		// A CredSSP step the attempt stopped waiting for is still inside IronRDP, using the handles below, so they go when
		// it comes back. Freeing one under a native call would take the process down.
		Task<GeneratorState>? abandoned = _abandoned;
		if (abandoned is { IsCompleted: false })
		{
			_ = abandoned.ContinueWith(
				finished => ReleaseNative(finished),
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}
		else
		{
			ReleaseNative(abandoned);
		}
	}

	[SuppressMessage(
		"Security",
		"CA5359:Do Not Disable Certificate Validation",
		Justification = "The certificate is judged by IHostIdentityVerifier right after the handshake and before CredSSP sends anything; RDP servers use self-signed certificates, which no chain check can accept.")]
	private static SslClientAuthenticationOptions TlsOptions(string host, bool literal) => new()
	{
		// A name goes out as SNI, which proxies in front of RDP route on. An address cannot.
		TargetHost = literal ? null : host,

		// As in IronRDP: CredSSP binds the login to this handshake's certificate, so every connect does a full one.
		AllowTlsResume = false,

		// A revocation list sits behind a URL the machine may not reach, and the lookup would hold the connect open.
		CertificateRevocationCheckMode = X509RevocationMode.NoCheck,

		// Almost every RDP server is self-signed. The certificate is judged right after the handshake, by the known
		// hosts and the user, before any login goes out.
		RemoteCertificateValidationCallback = static (_, _, _, _) => true,
	};

	/// <summary>
	/// The client's own address and port as IronRDP parses them, a Rust <c>SocketAddr</c>: no scope on an IPv6
	/// address, which only means something on this machine.
	/// </summary>
	private static string ClientAddress(Socket socket)
	{
		IPEndPoint local = socket.LocalEndPoint as IPEndPoint ?? new IPEndPoint(IPAddress.Any, 0);
		IPAddress address = local.Address.IsIPv4MappedToIPv6 ? local.Address.MapToIPv4() : local.Address;
		return address.AddressFamily == AddressFamily.InterNetworkV6
			? string.Create(CultureInfo.InvariantCulture, $"[{new IPAddress(address.GetAddressBytes())}]:{local.Port}")
			: string.Create(CultureInfo.InvariantCulture, $"{address}:{local.Port}");
	}

	private async Task<(HostIdentity? Identity, bool Verified)> CheckCertificateAsync(X509Certificate2? certificate, CancellationToken cancellationToken)
	{
		if (certificate is null)
		{
			return (null, false);
		}

		_progress.Enter(RdpConnectStage.Certificate);
		HostIdentity identity = TlsHostIdentity.Create(
			_progress.Host,
			_progress.Port,
			certificate,
			TlsHostIdentity.IsChainTrusted(certificate, _progress.Host));
		Stopwatch asked = Stopwatch.StartNew();
		bool verified = await _verifier.VerifyAsync(identity, cancellationToken);
		CertificatePrompted = asked.Elapsed >= PromptThreshold;
		if (!verified)
		{
			throw new ProtocolConnectException(
				ConnectFailure.HostIdentityRejected,
				$"The certificate {_progress.Endpoint} presented was not accepted, so no login was sent.");
		}

		return (identity, verified);
	}

	/// <summary>One connector step: the message it waits for, if any, then whatever it answers.</summary>
	private async Task StepAsync(Stream stream, CancellationToken cancellationToken)
	{
		ClientConnector connector = _connector!;
		byte[]? input = null;

		// A hint is a view of the connector's state too, so it goes before Step changes that state.
		using (PduHint? hint = connector.NextPduHint())
		{
			if (hint is not null)
			{
				input = await _reader!.ReadByHintAsync(hint, cancellationToken);
			}
		}

		_buffer!.Clear();
		using Written written = input is null ? connector.StepNoInput(_buffer) : connector.Step(input, _buffer);
		if (written.GetWrittenType() == WrittenType.Size)
		{
			await WriteAsync(stream, cancellationToken);
		}
	}

	/// <summary>
	/// Sends what the last step put in the buffer, straight to the stream. IronRDP's <c>Framed&lt;T&gt;.Write</c> holds
	/// a <see cref="Mutex"/> across its await, and releasing it from the thread the write resumed on throws.
	/// </summary>
	private async Task WriteAsync(Stream stream, CancellationToken cancellationToken)
	{
		byte[] bytes;
		using (VecU8 filled = _buffer!.GetFilled())
		{
			bytes = new byte[(int)filled.GetSize()];
			filled.Fill(bytes);
		}

		if (bytes.Length == 0)
		{
			return;
		}

		await stream.WriteAsync(bytes, cancellationToken);
		await stream.FlushAsync(cancellationToken);
	}

	/// <summary>
	/// CredSSP, as <c>ConnectionHelpers.PerformCredsspSteps</c> runs it: each server message goes through the sequence,
	/// whose generator may stop to have a Kerberos request sent, and whatever it answers goes back over TLS.
	/// </summary>
	private async Task RunCredsspAsync(byte[] publicKey, CancellationToken cancellationToken)
	{
		_credsspInit = CredsspSequence.Init(_connector!, _progress.Host, publicKey, null);
		_credssp = _credsspInit.GetCredsspSequence();
		TsRequest? request = _credsspInit.GetTsRequest();
		try
		{
			while (request is not null)
			{
				using (ClientState state = await ProcessAsync(request, cancellationToken))
				{
					request.Dispose();
					request = null;
					_buffer!.Clear();
					using Written written = _credssp.HandleProcessResult(state, _buffer);
					if (written.GetWrittenType() == WrittenType.Size)
					{
						await WriteAsync(_tls!, cancellationToken);
						LoginMessagesSent++;
					}
				}

				byte[] message;
				using (PduHint? hint = _credssp.NextPduHint())
				{
					if (hint is null)
					{
						break;
					}

					message = await _reader!.ReadByHintAsync(hint, cancellationToken);
				}

				request = _credssp.DecodeServerMessage(message);
			}
		}
		finally
		{
			request?.Dispose();
		}
	}

	/// <summary>
	/// Runs the generator for one server message to its end, sending the Kerberos requests it stops for. sspi-rs looks
	/// for a Kerberos server with a DNS query inside these calls, which a broken resolver can hold for seconds, so they
	/// run off this thread and the attempt stops waiting for them when its deadline passes.
	/// </summary>
	private async Task<ClientState> ProcessAsync(TsRequest request, CancellationToken cancellationToken)
	{
		CredsspProcessGenerator generator = _credssp!.ProcessTsRequest(request);
		_generator = generator;
		try
		{
			GeneratorState state = await RunNativeAsync(generator.Start, cancellationToken);
			while (true)
			{
				byte[] reply;
				using (state)
				{
					if (state.IsCompleted())
					{
						return state.GetClientStateIfCompleted();
					}

					if (!state.IsSuspended())
					{
						throw new InvalidOperationException("CredSSP stopped in a state that is neither waiting nor done.");
					}

					reply = await SendKerberosAsync(state, cancellationToken);
				}

				// The state is a value of its own, not a view of the generator, so it can go before the generator resumes.
				state = await RunNativeAsync(() => generator.Resume(reply), cancellationToken);
			}
		}
		finally
		{
			// The generator borrows the CredSSP sequence, so it goes before the sequence is used again, unless a step
			// is still running inside it.
			if (_abandoned is null)
			{
				generator.Dispose();
				_generator = null;
			}
		}
	}

	private async Task<byte[]> SendKerberosAsync(GeneratorState state, CancellationToken cancellationToken)
	{
		NetworkRequestProtocol protocol;
		string url;
		byte[] data;
		using (NetworkRequest request = state.GetNetworkRequestIfSuspended()
			?? throw new InvalidOperationException("CredSSP paused without a request to send."))
		{
			protocol = request.GetProtocol();
			url = request.GetUrl();
			using VecU8 payload = request.GetData();
			data = new byte[(int)payload.GetSize()];
			payload.Fill(data);
		}

		_progress.Enter(RdpConnectStage.Kerberos, RdpKerberosClient.Describe(url));
		byte[] reply = await RdpKerberosClient.SendAsync(protocol, url, data, cancellationToken);
		_progress.Enter(RdpConnectStage.Login);
		return reply;
	}

	private async Task<GeneratorState> RunNativeAsync(Func<GeneratorState> step, CancellationToken cancellationToken)
	{
		Task<GeneratorState> running = Task.Run(step, CancellationToken.None);
		try
		{
			return await running.WaitAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (!running.IsCompleted)
		{
			_abandoned = running;
			throw;
		}
	}

	private RdpTransport TakeTransport(bool credssp, HostIdentity? identity, bool verified)
	{
		ConnectionResult result;
		using (ClientConnectorState final = _connector!.ConsumeAndCastToClientConnectorState())
		{
			if (final.GetEnumType() != ClientConnectorStateType.Connected)
			{
				throw new ProtocolConnectException(
					ConnectFailure.ProtocolError,
					$"{_progress.Endpoint} ended the connection sequence without opening a session.");
			}

			result = final.GetConnectedResult();
		}

		try
		{
			// The server decides the desktop size, and a huge one would have the decoder allocate gigabytes.
			int width;
			int height;
			using (DesktopSize size = result.GetDesktopSize())
			{
				width = size.GetWidth();
				height = size.GetHeight();
			}

			if (!RdpDesktopBounds.IsSupported(width, height))
			{
				throw new ProtocolConnectException(ConnectFailure.ProtocolError, RdpDesktopBounds.DescribeRefusal(width, height));
			}

			RdpTransport transport = new(result, _tls!, _reader!, width, height, credssp, identity, verified);
			_tls = null;
			_network = null;
			_socket = null;
			_reader = null;
			return transport;
		}
		catch
		{
			result.Dispose();
			throw;
		}
	}

	private void ReleaseNative(Task<GeneratorState>? abandoned)
	{
		if (abandoned is { IsCompletedSuccessfully: true })
		{
			abandoned.Result.Dispose();
		}

		// Borrowers before what they borrow from: the generator from the CredSSP sequence, the sequence from the
		// connector's credentials.
		_generator?.Dispose();
		_credssp?.Dispose();
		_credsspInit?.Dispose();
		_connector?.Dispose();
		_buffer?.Dispose();
		_generator = null;
		_credssp = null;
		_credsspInit = null;
		_connector = null;
		_buffer = null;
	}
}
