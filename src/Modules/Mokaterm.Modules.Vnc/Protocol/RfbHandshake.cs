using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>
/// The client side of the RFB handshake: protocol version, security negotiation, authentication, ClientInit and
/// ServerInit. It runs entirely in .NET so passwords never reach the browser and TLS is a real TLS stack.
/// </summary>
internal sealed class RfbHandshake
{
	private readonly RfbWire _wire;
	private readonly VncHandshakeContext _context;

	private RfbHandshake(RfbWire wire, VncHandshakeContext context)
	{
		_wire = wire;
		_context = context;
	}

	/// <summary>Runs the handshake over <paramref name="stream"/> and returns the stream the session continues on.</summary>
	public static Task<RfbHandshakeResult> RunAsync(Stream stream, VncHandshakeContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(stream);
		ArgumentNullException.ThrowIfNull(context);
		return new RfbHandshake(new RfbWire(stream), context).RunAsync(cancellationToken);
	}

	private async Task<RfbHandshakeResult> RunAsync(CancellationToken cancellationToken)
	{
		RfbProtocolVersion version = await NegotiateVersionAsync(cancellationToken);

		_context.Status?.Report("Negotiating security");
		VncSecurityType security = await NegotiateSecurityAsync(version, cancellationToken);
		if (security == VncSecurityType.VeNCrypt)
		{
			security = await NegotiateVeNCryptAsync(cancellationToken);
		}

		_context.Status?.Report("Authenticating");
		await AuthenticateAsync(security, cancellationToken);
		await ReadSecurityResultAsync(version, security, cancellationToken);

		_context.Status?.Report("Opening the screen");
		await _wire.WriteByteAsync(_context.Shared ? (byte)1 : (byte)0, cancellationToken);
		RfbServerInit serverInit = await RfbServerInit.ReadAsync(_wire, cancellationToken);
		return new RfbHandshakeResult(_wire.Stream, serverInit, security);
	}

	private async Task<RfbProtocolVersion> NegotiateVersionAsync(CancellationToken cancellationToken)
	{
		byte[] greeting = await _wire.ReadBytesAsync(RfbProtocolVersion.Length, cancellationToken);
		if (!RfbProtocolVersion.TryParse(greeting, out RfbProtocolVersion server))
		{
			throw new VncProtocolException($"{_context.Endpoint} did not answer like a VNC server.");
		}

		if (server is { Major: 0, Minor: 0 })
		{
			throw new VncProtocolException($"{_context.Endpoint} is an UltraVNC repeater, which Mokaterm cannot use.");
		}

		if (!server.IsAtLeast(3, 3))
		{
			throw new VncProtocolException($"{_context.Endpoint} speaks RFB {server}, which is older than the oldest supported version 3.3.");
		}

		RfbProtocolVersion chosen = server.IsAtLeast(3, 8) ? RfbProtocolVersion.Latest
			: server.IsAtLeast(3, 7) ? new RfbProtocolVersion(3, 7)
			: new RfbProtocolVersion(3, 3);
		await _wire.WriteAsync(chosen.ToBytes(), cancellationToken);
		return chosen;
	}

	private async Task<VncSecurityType> NegotiateSecurityAsync(RfbProtocolVersion version, CancellationToken cancellationToken)
	{
		List<int> offered = [];
		if (version.IsAtLeast(3, 7))
		{
			int count = await _wire.ReadByteAsync(cancellationToken);
			if (count == 0)
			{
				throw Refused(await _wire.ReadReasonAsync(cancellationToken));
			}

			byte[] types = await _wire.ReadBytesAsync(count, cancellationToken);
			foreach (byte type in types)
			{
				offered.Add(type);
			}

			VncSecurityType selected = VncSecuritySelection.ChooseTopLevel(offered, _context.Encryption);
			if (selected == VncSecurityType.Invalid)
			{
				throw NoUsableSecurity(offered);
			}

			await _wire.WriteByteAsync((byte)selected, cancellationToken);
			return selected;
		}

		// RFB 3.3: the server decides alone and sends the type as a 32 bit number.
		uint chosen = await _wire.ReadUInt32Async(cancellationToken);
		if (chosen == 0)
		{
			throw Refused(await _wire.ReadReasonAsync(cancellationToken));
		}

		offered.Add((int)chosen);
		VncSecurityType dictated = VncSecuritySelection.ChooseTopLevel(offered, _context.Encryption);
		return dictated == VncSecurityType.Invalid ? throw NoUsableSecurity(offered) : dictated;
	}

	private async Task<VncSecurityType> NegotiateVeNCryptAsync(CancellationToken cancellationToken)
	{
		byte major = await _wire.ReadByteAsync(cancellationToken);
		byte minor = await _wire.ReadByteAsync(cancellationToken);
		if (major != 0 || minor < 2)
		{
			throw new VncProtocolException($"{_context.Endpoint} speaks VeNCrypt {major}.{minor}; Mokaterm needs 0.2.");
		}

		await _wire.WriteAsync(new byte[] { 0, 2 }, cancellationToken);
		if (await _wire.ReadByteAsync(cancellationToken) != 0)
		{
			throw new VncProtocolException($"{_context.Endpoint} refused VeNCrypt version 0.2.");
		}

		int count = await _wire.ReadByteAsync(cancellationToken);
		if (count == 0)
		{
			throw new VncProtocolException($"{_context.Endpoint} offers no VeNCrypt security types.");
		}

		byte[] raw = await _wire.ReadBytesAsync(count * 4, cancellationToken);
		List<int> offered = new(count);
		for (int i = 0; i < count; i++)
		{
			offered.Add((int)BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(i * 4, 4)));
		}

		VncSecurityType subtype = VncSecuritySelection.ChooseSubtype(offered, _context.Encryption, HasUsername);
		if (subtype == VncSecurityType.Invalid)
		{
			throw NoUsableSecurity(offered);
		}

		await _wire.WriteUInt32Async((uint)subtype, cancellationToken);
		if (VncSecurityTypes.IsEncrypted(subtype))
		{
			if (await _wire.ReadByteAsync(cancellationToken) == 0)
			{
				throw new VncProtocolException($"{_context.Endpoint} refused to start TLS for {subtype}.");
			}

			_context.Status?.Report("Starting TLS");
			_wire.Stream = await VncTlsUpgrade.AuthenticateAsync(
				_wire.Stream,
				_context.Host,
				_context.Port,
				VncSecurityTypes.UsesCertificate(subtype),
				_context.Verifier,
				cancellationToken);
		}

		return subtype;
	}

	private async Task AuthenticateAsync(VncSecurityType security, CancellationToken cancellationToken)
	{
		if (VncSecurityTypes.NeedsPassword(security))
		{
			await AnswerChallengeAsync(cancellationToken);
		}
		else if (VncSecurityTypes.NeedsUsername(security))
		{
			await SendPlainCredentialsAsync(cancellationToken);
		}
	}

	private async Task AnswerChallengeAsync(CancellationToken cancellationToken)
	{
		byte[] challenge = await _wire.ReadBytesAsync(VncPasswordAuthentication.ChallengeLength, cancellationToken);
		SecretBuffer password = _context.Credentials?.Password ?? throw MissingPassword();

		byte[] response;
		try
		{
			response = VncPasswordAuthentication.CreateResponse(challenge, password.Span);
		}
		catch (CryptographicException ex)
		{
			// DES refuses its handful of weak keys, which a password of eight identical or empty bytes can produce.
			throw new ProtocolConnectException(
				ConnectFailure.AuthenticationFailed,
				"This password cannot be used for VNC authentication because it makes a key DES rejects. Change the password on the server.",
				ex);
		}

		await _wire.WriteAsync(response, cancellationToken);
	}

	private async Task SendPlainCredentialsAsync(CancellationToken cancellationToken)
	{
		LoginCredentials? credentials = _context.Credentials;
		if (credentials is null || string.IsNullOrEmpty(credentials.Username))
		{
			throw new ProtocolConnectException(
				ConnectFailure.AuthenticationFailed,
				$"{_context.Endpoint} asks for a user name and password. Edit the login and set the user name.");
		}

		SecretBuffer password = credentials.Password
			?? throw new ProtocolConnectException(
				ConnectFailure.AuthenticationFailed,
				$"{_context.Endpoint} asks for a user name and password. Edit the login and set a password.");

		byte[] user = Encoding.UTF8.GetBytes(credentials.Username);
		byte[] message = new byte[8 + user.Length + password.Length];
		try
		{
			BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(0, 4), (uint)user.Length);
			BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(4, 4), (uint)password.Length);
			user.CopyTo(message.AsSpan(8));
			password.Span.CopyTo(message.AsSpan(8 + user.Length));
			await _wire.WriteAsync(message, cancellationToken);
		}
		finally
		{
			// The password travelled in plain text inside this buffer; only TLS kept it off the wire.
			CryptographicOperations.ZeroMemory(message);
		}
	}

	private async Task ReadSecurityResultAsync(RfbProtocolVersion version, VncSecurityType security, CancellationToken cancellationToken)
	{
		// Before 3.8 a server that asked for nothing also reports nothing.
		if (security == VncSecurityType.None && !version.IsAtLeast(3, 8))
		{
			return;
		}

		uint status = await _wire.ReadUInt32Async(cancellationToken);
		if (status == 0)
		{
			return;
		}

		string reason = version.IsAtLeast(3, 8) ? await _wire.ReadReasonAsync(cancellationToken) : "";
		bool needsCredentials = VncSecurityTypes.NeedsPassword(security) || VncSecurityTypes.NeedsUsername(security);
		string message = reason.Length > 0
			? $"{_context.Endpoint} refused the login: {reason.TrimEnd('.')}."
			: status == 2
				? $"{_context.Endpoint} refused the login and is now blocking further attempts."
				: $"{_context.Endpoint} refused the login.";

		// Status 2 means the server stopped accepting attempts, so asking for another password would not help.
		if (needsCredentials && status != 2)
		{
			throw new VncAuthenticationException(message);
		}

		throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, message);
	}

	private bool HasUsername => !string.IsNullOrEmpty(_context.Credentials?.Username);

	// Naming the setting matters: a login set to send nothing looks the same from here as one whose password is missing.
	private ProtocolConnectException MissingPassword() => new(
		ConnectFailure.AuthenticationFailed,
		_context.Credentials?.Method == AuthenticationMethod.Anonymous
			? $"{_context.Endpoint} asks for a VNC password, but this login is set to connect without one. Edit the login, set Authentication to Password and enter the server's VNC password."
			: $"{_context.Endpoint} asks for a VNC password. Edit the login and set one.");

	private ProtocolConnectException Refused(string reason) => new(
		ConnectFailure.ProtocolError,
		reason.Length > 0
			? $"{_context.Endpoint} refused the connection: {reason.TrimEnd('.')}."
			: $"{_context.Endpoint} refused the connection without giving a reason.");

	private ProtocolConnectException NoUsableSecurity(IEnumerable<int> offered)
	{
		string names = string.Join(", ", offered.Select(VncSecurityTypes.Name));
		string message = _context.Encryption == VncEncryptionMode.Required
			? $"This login requires an encrypted VNC session, and {_context.Endpoint} offers none (security types: {names})."
			: $"{_context.Endpoint} offers no security type Mokaterm can use (security types: {names}).";
		return new ProtocolConnectException(ConnectFailure.ProtocolError, message);
	}
}
