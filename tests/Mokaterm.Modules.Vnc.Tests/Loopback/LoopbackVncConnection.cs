using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace Mokaterm.Modules.Vnc.Tests.Loopback;

/// <summary>One accepted connection past its handshake, with what the client said along the way.</summary>
internal sealed class LoopbackVncConnection : IAsyncDisposable
{
	private readonly TcpClient _client;

	public LoopbackVncConnection(TcpClient client, Stream stream)
	{
		_client = client;
		Stream = stream;
	}

	/// <summary>The stream the session runs on, TLS wrapped when a VeNCrypt subtype asked for it.</summary>
	public Stream Stream { get; private set; }

	/// <summary>Replaces the stream after a TLS upgrade. The new stream owns the old one.</summary>
	public void SetStream(Stream stream) => Stream = stream;

	public string ClientVersion { get; set; } = "";

	public int ChosenSecurity { get; set; }

	public int ChosenSubtype { get; set; }

	public byte? SharedFlag { get; set; }

	public string? PlainUsername { get; set; }

	public bool PasswordAccepted { get; set; }

	public bool IsEncrypted { get; set; }

	public byte[] ServerInit { get; set; } = [];

	public async Task<byte[]> ReadExactlyAsync(int count, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[count];
		await Stream.ReadExactlyAsync(buffer, cancellationToken);
		return buffer;
	}

	public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		await Stream.WriteAsync(data, cancellationToken);
		await Stream.FlushAsync(cancellationToken);
	}

	/// <summary>Sends a Bell message, which is the shortest thing a server can push at a client.</summary>
	public Task SendBellAsync(CancellationToken cancellationToken) => SendAsync(new byte[] { 2 }, cancellationToken);

	/// <summary>Sends a ServerCutText message with <paramref name="text"/>, useful as a payload of a known size.</summary>
	public Task SendCutTextAsync(string text, CancellationToken cancellationToken)
	{
		byte[] body = Encoding.Latin1.GetBytes(text);
		byte[] message = new byte[8 + body.Length];
		message[0] = 3;
		BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(4, 4), (uint)body.Length);
		body.CopyTo(message.AsSpan(8));
		return SendAsync(message, cancellationToken);
	}

	public async ValueTask DisposeAsync()
	{
		await Stream.DisposeAsync();
		_client.Dispose();
	}
}
