using System.Net.Sockets;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>Opens the TCP connection a telnet session runs on.</summary>
internal sealed class TelnetConnector
{
	private readonly TimeSpan _connectTimeout;

	public TelnetConnector(string host, int port, TimeSpan connectTimeout)
	{
		Host = host;
		Port = port;
		_connectTimeout = connectTimeout;
	}

	public string Host { get; }

	public int Port { get; }

	/// <exception cref="ProtocolConnectException">The host could not be reached.</exception>
	public async Task<TelnetTransport> ConnectAsync(IProgress<string>? status, CancellationToken cancellationToken)
	{
		status?.Report($"Connecting to {Host}:{Port}");
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_connectTimeout);

		TcpClient client = new();
		try
		{
			// Nagle would hold single keystrokes back until the server answered.
			client.NoDelay = true;
			await client.ConnectAsync(Host, Port, timeout.Token);
			return new TelnetTransport(client, Host, Port);
		}
		catch (Exception ex)
		{
			client.Dispose();
			if (cancellationToken.IsCancellationRequested)
			{
				throw;
			}

			bool timedOut = ex is OperationCanceledException && timeout.IsCancellationRequested;
			throw await TelnetConnectErrors.DescribeAsync(ex, Host, Port, timedOut, cancellationToken);
		}
	}
}
