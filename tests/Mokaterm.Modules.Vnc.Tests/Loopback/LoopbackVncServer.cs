using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Mokaterm.Modules.Vnc.Protocol;

namespace Mokaterm.Modules.Vnc.Tests.Loopback;

/// <summary>
/// A small RFB server on the loopback interface. It speaks the handshake the way real servers do, records what the
/// client sent and hands the accepted connection to the test so it can drive the session bytes by hand.
/// </summary>
internal sealed class LoopbackVncServer : IAsyncDisposable
{
	private readonly LoopbackVncServerOptions _options;
	private readonly TcpListener _listener;
	private readonly CancellationTokenSource _stopping = new();
	private readonly Channel<LoopbackVncConnection> _accepted = Channel.CreateUnbounded<LoopbackVncConnection>();
	private readonly Task _loop;
	private int _attempts;
	private int _disposed;

	public LoopbackVncServer(LoopbackVncServerOptions options)
	{
		_options = options;
		_listener = new TcpListener(IPAddress.Loopback, 0);
		_listener.Start();
		Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
		_loop = AcceptLoopAsync(_stopping.Token);
	}

	public int Port { get; }

	/// <summary>How many clients connected, handshake refused ones included.</summary>
	public int Attempts => Volatile.Read(ref _attempts);

	/// <summary>The reason the last handshake failed, for tests that expect the server to refuse.</summary>
	public string? LastFailure { get; private set; }

	/// <summary>Waits for the next connection that finished the handshake.</summary>
	public ValueTask<LoopbackVncConnection> AcceptedAsync(CancellationToken cancellationToken) =>
		_accepted.Reader.ReadAsync(cancellationToken);

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await _stopping.CancelAsync();
		_listener.Stop();
		try
		{
			await _loop;
		}
		catch (Exception)
		{
			// The loop is torn down with the listener.
		}

		while (_accepted.Reader.TryRead(out LoopbackVncConnection? connection))
		{
			await connection.DisposeAsync();
		}

		// The certificate belongs to the test that made it.
		_stopping.Dispose();
	}

	private static byte[] BuildServerInit(LoopbackVncServerOptions options)
	{
		byte[] name = Encoding.UTF8.GetBytes(options.DesktopName);
		byte[] message = new byte[24 + name.Length];
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(0, 2), (ushort)options.Width);
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2, 2), (ushort)options.Height);
		message[4] = 32;  // bits per pixel
		message[5] = 24;  // depth
		message[6] = 0;   // little endian
		message[7] = 1;   // true colour
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(8, 2), 255);
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(10, 2), 255);
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(12, 2), 255);
		message[14] = 16;
		message[15] = 8;
		message[16] = 0;
		BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(20, 4), (uint)name.Length);
		name.CopyTo(message.AsSpan(24));
		return message;
	}

	private static async Task WriteAsync(Stream stream, byte[] data, CancellationToken cancellationToken)
	{
		await stream.WriteAsync(data, cancellationToken);
		await stream.FlushAsync(cancellationToken);
	}

	private static async Task<byte[]> ReadAsync(Stream stream, int count, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[count];
		await stream.ReadExactlyAsync(buffer, cancellationToken);
		return buffer;
	}

	private static async Task WriteReasonAsync(Stream stream, string reason, CancellationToken cancellationToken)
	{
		byte[] text = Encoding.UTF8.GetBytes(reason);
		byte[] message = new byte[4 + text.Length];
		BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(0, 4), (uint)text.Length);
		text.CopyTo(message.AsSpan(4));
		await WriteAsync(stream, message, cancellationToken);
	}

	private async Task AcceptLoopAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await _listener.AcceptTcpClientAsync(cancellationToken);
			}
			catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
			{
				return;
			}

			int attempt = Interlocked.Increment(ref _attempts);

			// Windows grows the send buffer on its own and would swallow megabytes, which hides whether the client
			// stopped reading. A fixed small buffer makes a blocked reader block the writer.
			client.SendBufferSize = 8 * 1024;
			_ = HandshakeAsync(client, attempt, cancellationToken);
		}
	}

	private async Task HandshakeAsync(TcpClient client, int attempt, CancellationToken cancellationToken)
	{
		Stream stream = client.GetStream();
		LoopbackVncConnection connection = new(client, stream);
		try
		{
			if (attempt >= _options.SilentFromAttempt)
			{
				// Held open without a greeting until the server stops, like a machine that hangs after accepting.
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			}

			await WriteAsync(stream, Encoding.ASCII.GetBytes(_options.Greeting), cancellationToken);
			if (_options.CloseAfterGreeting)
			{
				await connection.DisposeAsync();
				return;
			}

			byte[] greeting = await ReadAsync(stream, 12, cancellationToken);
			connection.ClientVersion = Encoding.ASCII.GetString(greeting);
			if (!RfbProtocolVersion.TryParse(greeting, out RfbProtocolVersion version))
			{
				throw new InvalidOperationException($"The client greeted with '{connection.ClientVersion}'.");
			}

			int security = await NegotiateSecurityAsync(connection, version, cancellationToken);
			if (security == 0)
			{
				await connection.DisposeAsync();
				return;
			}

			if (security == (int)VncSecurityType.VeNCrypt)
			{
				security = await NegotiateVeNCryptAsync(connection, cancellationToken);
				if (security == 0)
				{
					await connection.DisposeAsync();
					return;
				}
			}

			if (!await AuthenticateAsync(connection, version, (VncSecurityType)security, cancellationToken))
			{
				await connection.DisposeAsync();
				return;
			}

			connection.SharedFlag = (await ReadAsync(connection.Stream, 1, cancellationToken))[0];
			connection.ServerInit = BuildServerInit(_options);
			await WriteAsync(connection.Stream, connection.ServerInit, cancellationToken);
			await _accepted.Writer.WriteAsync(connection, cancellationToken);
		}
		catch (Exception ex)
		{
			LastFailure = ex.Message;
			await connection.DisposeAsync();
		}
	}

	private async Task<int> NegotiateSecurityAsync(LoopbackVncConnection connection, RfbProtocolVersion version, CancellationToken cancellationToken)
	{
		if (!version.IsAtLeast(3, 7))
		{
			// RFB 3.3: the server dictates one type and sends it as a 32 bit number.
			byte[] dictated = new byte[4];
			BinaryPrimitives.WriteUInt32BigEndian(dictated, (uint)_options.SecurityTypes[0]);
			await WriteAsync(connection.Stream, dictated, cancellationToken);
			connection.ChosenSecurity = _options.SecurityTypes[0];
			return connection.ChosenSecurity;
		}

		if (_options.RefuseReason is { } reason)
		{
			await WriteAsync(connection.Stream, [0], cancellationToken);
			await WriteReasonAsync(connection.Stream, reason, cancellationToken);
			return 0;
		}

		byte[] offer = new byte[1 + _options.SecurityTypes.Count];
		offer[0] = (byte)_options.SecurityTypes.Count;
		for (int i = 0; i < _options.SecurityTypes.Count; i++)
		{
			offer[i + 1] = (byte)_options.SecurityTypes[i];
		}

		await WriteAsync(connection.Stream, offer, cancellationToken);
		connection.ChosenSecurity = (await ReadAsync(connection.Stream, 1, cancellationToken))[0];
		return connection.ChosenSecurity;
	}

	private async Task<int> NegotiateVeNCryptAsync(LoopbackVncConnection connection, CancellationToken cancellationToken)
	{
		await WriteAsync(connection.Stream, [_options.VeNCryptVersion.Major, _options.VeNCryptVersion.Minor], cancellationToken);
		await ReadAsync(connection.Stream, 2, cancellationToken);
		await WriteAsync(connection.Stream, [0], cancellationToken);

		byte[] offer = new byte[1 + (_options.VeNCryptSubtypes.Count * 4)];
		offer[0] = (byte)_options.VeNCryptSubtypes.Count;
		for (int i = 0; i < _options.VeNCryptSubtypes.Count; i++)
		{
			BinaryPrimitives.WriteUInt32BigEndian(offer.AsSpan(1 + (i * 4), 4), (uint)_options.VeNCryptSubtypes[i]);
		}

		await WriteAsync(connection.Stream, offer, cancellationToken);
		int subtype = (int)BinaryPrimitives.ReadUInt32BigEndian(await ReadAsync(connection.Stream, 4, cancellationToken));
		connection.ChosenSubtype = subtype;
		if (!VncSecurityTypes.IsEncrypted((VncSecurityType)subtype))
		{
			return subtype;
		}

		await WriteAsync(connection.Stream, [1], cancellationToken);
		SslStream tls = new(connection.Stream, leaveInnerStreamOpen: false);
		await tls.AuthenticateAsServerAsync(
			new SslServerAuthenticationOptions
			{
				ServerCertificate = _options.Certificate ?? throw new InvalidOperationException("The test server needs a certificate for this subtype."),
				ClientCertificateRequired = false,
			},
			cancellationToken);
		connection.SetStream(tls);
		connection.IsEncrypted = true;
		return subtype;
	}

	private async Task<bool> AuthenticateAsync(
		LoopbackVncConnection connection,
		RfbProtocolVersion version,
		VncSecurityType security,
		CancellationToken cancellationToken)
	{
		bool accepted = true;
		if (VncSecurityTypes.NeedsPassword(security))
		{
			byte[] challenge = RandomNumberGenerator.GetBytes(VncPasswordAuthentication.ChallengeLength);
			await WriteAsync(connection.Stream, challenge, cancellationToken);
			byte[] answer = await ReadAsync(connection.Stream, VncPasswordAuthentication.ChallengeLength, cancellationToken);
			byte[] expected = VncPasswordAuthentication.CreateResponse(challenge, Encoding.UTF8.GetBytes(_options.Password ?? ""));
			accepted = answer.AsSpan().SequenceEqual(expected);
		}
		else if (VncSecurityTypes.NeedsUsername(security))
		{
			byte[] header = await ReadAsync(connection.Stream, 8, cancellationToken);
			int userLength = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
			int passwordLength = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4));
			connection.PlainUsername = Encoding.UTF8.GetString(await ReadAsync(connection.Stream, userLength, cancellationToken));
			string password = Encoding.UTF8.GetString(await ReadAsync(connection.Stream, passwordLength, cancellationToken));
			accepted = connection.PlainUsername == (_options.Username ?? "") && password == (_options.Password ?? "");
		}

		connection.PasswordAccepted = accepted;
		uint result = _options.ForcedSecurityResult ?? (accepted ? 0u : 1u);
		if (security == VncSecurityType.None && !version.IsAtLeast(3, 8))
		{
			return result == 0;
		}

		byte[] status = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(status, result);
		await WriteAsync(connection.Stream, status, cancellationToken);
		if (result != 0 && version.IsAtLeast(3, 8))
		{
			await WriteReasonAsync(connection.Stream, "Authentication failed", cancellationToken);
		}

		return result == 0;
	}
}
