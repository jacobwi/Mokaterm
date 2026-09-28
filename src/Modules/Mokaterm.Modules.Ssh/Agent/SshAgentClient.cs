using System.Buffers.Binary;
using System.Globalization;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Principal;

namespace Mokaterm.Modules.Ssh.Agent;

/// <summary>
/// The SSH agent protocol over one connection. Requests are synchronous because SSH.NET signs on its own thread and
/// reads the result as soon as the call returns. Each request has a deadline: an agent that accepts a connection and then
/// never answers would otherwise hold a connect, and SSH.NET's thread with it, forever.
/// </summary>
internal sealed class SshAgentClient : IDisposable
{
	public const byte RequestIdentities = 11;

	public const byte IdentitiesAnswer = 12;

	public const byte SignRequest = 13;

	public const byte SignResponse = 14;

	public const byte Failure = 5;

	/// <summary>Ask an RSA key for a SHA-256 signature (<c>rsa-sha2-256</c>) instead of the SHA-1 default.</summary>
	public const uint FlagRsaSha2256 = 2;

	/// <summary>Ask an RSA key for a SHA-512 signature (<c>rsa-sha2-512</c>).</summary>
	public const uint FlagRsaSha2512 = 4;

	/// <summary>Agents cap their own frames well below this; it only stops a broken peer from allocating the heap.</summary>
	private const int MaxFrameBytes = 1 << 20;

	private const int MaxIdentities = 2048;

	private const int MinTimeoutMilliseconds = 100;

	private const int MaxTimeoutMilliseconds = 30_000;

	private readonly Stream _stream;
	private readonly IDisposable? _owned;
	private readonly TimeSpan _timeout;

	private SshAgentClient(Stream stream, IDisposable? owned, TimeSpan timeout)
	{
		_stream = stream;
		_owned = owned;
		_timeout = timeout;
	}

	/// <summary>Opens the first agent that answers, or throws when none does.</summary>
	/// <exception cref="SshAgentException">No agent is listening.</exception>
	public static SshAgentClient Connect(IReadOnlyList<SshAgentAddress> addresses, TimeSpan timeout)
	{
		ArgumentNullException.ThrowIfNull(addresses);
		Exception? last = null;
		foreach (SshAgentAddress address in addresses)
		{
			try
			{
				return Connect(address, timeout);
			}
			catch (SshAgentException ex)
			{
				last = ex;
			}
		}

		throw new SshAgentException(
			addresses.Count == 0
				? "No SSH agent was found. Start ssh-agent, or name one in the SSH settings."
				: "No SSH agent answered.",
			last);
	}

	/// <param name="timeout">How long connecting, and then each request, may take.</param>
	/// <exception cref="SshAgentException">The agent is not listening or refused the connection.</exception>
	public static SshAgentClient Connect(SshAgentAddress address, TimeSpan timeout)
	{
		ArgumentNullException.ThrowIfNull(address);
		int milliseconds = (int)Math.Clamp(timeout.TotalMilliseconds, MinTimeoutMilliseconds, MaxTimeoutMilliseconds);
		return address.Transport == SshAgentTransport.NamedPipe
			? ConnectPipe(address, milliseconds)
			: ConnectSocket(address, milliseconds);
	}

	/// <summary>Every public key the agent holds, in the order it lists them.</summary>
	/// <exception cref="SshAgentException">The agent refused or answered with something else.</exception>
	public IReadOnlyList<SshAgentIdentity> ListIdentities()
	{
		SshWireReader reader = Request(new SshWireWriter().WriteByte(RequestIdentities).ToArray(), IdentitiesAnswer);
		if (!reader.TryReadUInt32(out uint count) || count > MaxIdentities)
		{
			throw new SshAgentException("The agent's key list could not be read.");
		}

		List<SshAgentIdentity> identities = new((int)count);
		for (uint i = 0; i < count; i++)
		{
			if (!reader.TryReadString(out byte[] blob) || !reader.TryReadText(out string comment))
			{
				throw new SshAgentException("The agent's key list ended early.");
			}

			identities.Add(new SshAgentIdentity { KeyBlob = blob, Comment = comment });
		}

		return identities;
	}

	/// <summary>
	/// Signs <paramref name="data"/> with the key the agent holds for <paramref name="keyBlob"/>. The answer is the
	/// wire-format signature blob (algorithm name then signature), which is exactly what an SSH.NET host algorithm returns.
	/// </summary>
	/// <exception cref="SshAgentException">The agent does not hold the key, or refused to sign.</exception>
	public byte[] Sign(ReadOnlySpan<byte> keyBlob, ReadOnlySpan<byte> data, uint flags)
	{
		byte[] request = new SshWireWriter()
			.WriteByte(SignRequest)
			.WriteString(keyBlob)
			.WriteString(data)
			.WriteUInt32(flags)
			.ToArray();

		SshWireReader reader = Request(request, SignResponse);
		if (!reader.TryReadString(out byte[] signature) || signature.Length == 0)
		{
			throw new SshAgentException("The agent's signature could not be read.");
		}

		return signature;
	}

	public void Dispose()
	{
		_stream.Dispose();
		_owned?.Dispose();
	}

	private static SshAgentClient ConnectPipe(SshAgentAddress address, int milliseconds)
	{
		// Asynchronous, so a read can be given up on. Identification only: without it Windows lets whoever serves the pipe
		// impersonate this user, and Pageant pipes are found by name, which any local account can take.
		NamedPipeClientStream pipe = new(
			".",
			address.Name,
			PipeDirection.InOut,
			PipeOptions.Asynchronous,
			OperatingSystem.IsWindows() ? TokenImpersonationLevel.Identification : TokenImpersonationLevel.None);
		try
		{
			pipe.Connect(milliseconds);
			return new SshAgentClient(pipe, null, TimeSpan.FromMilliseconds(milliseconds));
		}
		catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
		{
			pipe.Dispose();
			throw new SshAgentException($"The SSH agent at {address.Display} did not accept a connection: {ex.Message}", ex);
		}
	}

	private static SshAgentClient ConnectSocket(SshAgentAddress address, int milliseconds)
	{
		Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
		{
			SendTimeout = milliseconds,
			ReceiveTimeout = milliseconds,
		};

		try
		{
			socket.Connect(new UnixDomainSocketEndPoint(address.Name));
			NetworkStream stream = new(socket, ownsSocket: false);
			return new SshAgentClient(stream, socket, TimeSpan.FromMilliseconds(milliseconds));
		}
		catch (Exception ex) when (ex is SocketException or IOException or ArgumentException or PlatformNotSupportedException)
		{
			socket.Dispose();
			throw new SshAgentException($"The SSH agent at {address.Display} did not accept a connection: {ex.Message}", ex);
		}
	}

	private SshWireReader Request(byte[] body, byte expectedResponse)
	{
		byte[] frame = new byte[sizeof(uint) + body.Length];
		BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)body.Length);
		body.CopyTo(frame, sizeof(uint));

		// One deadline for the whole exchange, so an agent that trickles its answer cannot stretch it either.
		using CancellationTokenSource deadline = new(_timeout);
		try
		{
			Complete(_stream.WriteAsync(frame, deadline.Token).AsTask(), deadline);
			Complete(_stream.FlushAsync(deadline.Token), deadline);
		}
		catch (Exception ex) when (ex is IOException or ObjectDisposedException)
		{
			throw new SshAgentException($"The SSH agent closed the connection: {ex.Message}", ex);
		}

		byte[] response = ReadFrame(deadline);
		SshWireReader reader = new(response);
		if (!reader.TryReadByte(out byte type))
		{
			throw new SshAgentException("The agent sent an empty answer.");
		}

		if (type == expectedResponse)
		{
			return reader;
		}

		throw new SshAgentException(type == Failure
			? "The SSH agent refused the request. The key may be gone, or a confirmation was declined."
			: string.Create(CultureInfo.InvariantCulture, $"The SSH agent answered with message type {type}, which is not expected here."));
	}

	private byte[] ReadFrame(CancellationTokenSource deadline)
	{
		byte[] header = new byte[sizeof(uint)];
		ReadExactly(header, deadline);
		uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
		if (length is 0 or > MaxFrameBytes)
		{
			throw new SshAgentException("The agent announced a message length this client will not read.");
		}

		byte[] body = new byte[length];
		ReadExactly(body, deadline);
		return body;
	}

	private void ReadExactly(byte[] buffer, CancellationTokenSource deadline)
	{
		try
		{
			Complete(_stream.ReadExactlyAsync(buffer, deadline.Token).AsTask(), deadline);
		}
		catch (Exception ex) when (ex is EndOfStreamException or IOException or ObjectDisposedException)
		{
			throw new SshAgentException($"The SSH agent closed the connection: {ex.Message}", ex);
		}
	}

	/// <summary>Waits for one step of a request; the callers are synchronous, SSH.NET's signing thread among them.</summary>
	private void Complete(Task step, CancellationTokenSource deadline)
	{
		try
		{
			step.GetAwaiter().GetResult();
		}
		catch (OperationCanceledException ex) when (deadline.IsCancellationRequested)
		{
			throw new SshAgentException(
				string.Create(CultureInfo.InvariantCulture, $"The SSH agent did not answer within {_timeout.TotalSeconds:0.#} seconds."),
				ex);
		}
	}
}
