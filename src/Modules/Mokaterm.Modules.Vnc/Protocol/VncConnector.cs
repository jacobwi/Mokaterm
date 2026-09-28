using System.Net.Sockets;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>
/// Dials a VNC server and runs the RFB handshake. The session keeps one so it can open the connection again after
/// its view was unmounted, for example while the vault was locked.
/// </summary>
internal sealed class VncConnector
{
	private readonly IHostIdentityVerifier _verifier;

	public VncConnector(string host, int port, VncConnectionOptions options, TimeSpan timeout, IHostIdentityVerifier verifier)
	{
		ArgumentNullException.ThrowIfNull(options);
		Host = host;
		Port = port;
		Options = options;
		Timeout = timeout;
		_verifier = verifier;
	}

	public string Host { get; }

	public int Port { get; }

	public VncConnectionOptions Options { get; }

	/// <summary>Covers the TCP connect and the handshake, but not the time a certificate prompt is on screen.</summary>
	public TimeSpan Timeout { get; }

	/// <exception cref="ProtocolConnectException">Connecting failed in a way the user should see.</exception>
	/// <exception cref="VncAuthenticationException">The server refused the credentials and another password may work.</exception>
	public async Task<VncTransport> ConnectAsync(LoginCredentials? credentials, IProgress<string>? status, CancellationToken cancellationToken)
	{
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(Timeout);
		TcpClient client = new();
		try
		{
			status?.Report("Connecting");
			await client.ConnectAsync(Host, Port, deadline.Token);

			// Input events and screen updates are small and latency bound; Nagle would hold them back.
			client.NoDelay = true;

			NetworkStream network = client.GetStream();
			RfbHandshakeResult handshake = await RfbHandshake.RunAsync(
				network,
				new VncHandshakeContext
				{
					Host = Host,
					Port = Port,
					Verifier = new DeadlinePausingVerifier(_verifier, deadline, Timeout, cancellationToken),
					Encryption = Options.Encryption,
					Shared = Options.Shared,
					Credentials = credentials,
					Status = status,
				},
				deadline.Token);

			return new VncTransport(client, network, handshake);
		}
		catch (Exception ex)
		{
			client.Dispose();

			// A refused password is the connector's caller's business, and a cancel by the user is not a failure.
			if (ex is VncAuthenticationException || (ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				throw;
			}

			throw await VncConnectErrors.DescribeAsync(ex, Host, Port, deadline.IsCancellationRequested, CancellationToken.None);
		}
	}
}
