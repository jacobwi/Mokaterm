using System.Net.Sockets;

namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>
/// One live RFB connection past its handshake: the socket, the stream the session reads and writes (TLS wrapped
/// for the encrypted VeNCrypt subtypes) and the ServerInit the page side is given.
/// </summary>
internal sealed class VncTransport : IAsyncDisposable
{
	private readonly TcpClient _client;
	private readonly Stream _network;
	private int _disposed;

	public VncTransport(TcpClient client, Stream network, RfbHandshakeResult handshake)
	{
		ArgumentNullException.ThrowIfNull(handshake);
		_client = client;
		_network = network;
		Stream = handshake.Stream;
		ServerInit = handshake.ServerInit;
		Security = handshake.Security;
	}

	/// <summary>The stream the relay reads from and writes to.</summary>
	public Stream Stream { get; }

	public RfbServerInit ServerInit { get; }

	public VncSecurityType Security { get; }

	public bool IsEncrypted => VncSecurityTypes.IsEncrypted(Security);

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		if (!ReferenceEquals(Stream, _network))
		{
			// The TLS stream leaves its inner stream open, so both are closed here.
			await CloseAsync(Stream);
		}

		await CloseAsync(_network);
		_client.Dispose();
	}

	private static async ValueTask CloseAsync(Stream stream)
	{
		try
		{
			await stream.DisposeAsync();
		}
		catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
		{
			// Closing a connection the other side already dropped fails again; it is gone either way.
		}
	}
}
