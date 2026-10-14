using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Rdp.Protocol;
using Mokaterm.Modules.Rdp.Tests.Fakes;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>
/// What connecting does when there is no RDP server: the failures a user actually meets, against loopback ports
/// this test owns.
/// </summary>
public sealed class RdpConnectTests
{
	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ADeadPort_SaysTheHostRefusedIt()
	{
		int port = LoopbackPort.Free();
		ProtocolConnectContext context = RdpHarness.CreateContext(port);

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => RdpHarness.CreateProvider().ConnectAsync(context, TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.HostUnreachable, error.Failure);
		Assert.Contains(port.ToString(CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_AHostThatDoesNotExist_SaysTheNameIsWrong()
	{
		ProtocolConnectContext context = RdpHarness.CreateContext(3389, address: "mokaterm-no-such-host.invalid");

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => RdpHarness.CreateProvider().ConnectAsync(context, TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.HostUnreachable, error.Failure);
		Assert.Contains("mokaterm-no-such-host.invalid", error.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_AServerThatIsNotRdp_SaysSo()
	{
		using JunkListener listener = JunkListener.Start();
		ProtocolConnectContext context = RdpHarness.CreateContext(listener.Port);

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => RdpHarness.CreateProvider().ConnectAsync(context, TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.ProtocolError, error.Failure);
		Assert.Contains("not RDP", error.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_AServerThatSaysNothing_TimesOut()
	{
		using JunkListener listener = JunkListener.Start(answer: false);
		FakeSettingsService settings = new();
		settings.Set(new RdpSettings { ConnectTimeoutSeconds = 1 });
		ProtocolConnectContext context = RdpHarness.CreateContext(listener.Port);

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => RdpHarness.CreateProvider(settings).ConnectAsync(context, TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.Timeout, error.Failure);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_AServerThatHangsUp_SaysTheConnectionBroke()
	{
		using JunkListener listener = JunkListener.Start(answer: false, closeAtOnce: true);
		ProtocolConnectContext context = RdpHarness.CreateContext(listener.Port);

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => RdpHarness.CreateProvider().ConnectAsync(context, TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.ProtocolError, error.Failure);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ReportsProgressBeforeItFails()
	{
		RecordingProgress<string> status = new();
		ProtocolConnectContext context = RdpHarness.CreateContext(LoopbackPort.Free(), status: status);

		await Assert.ThrowsAsync<ProtocolConnectException>(
			() => RdpHarness.CreateProvider().ConnectAsync(context, TestContext.Current.CancellationToken));

		Assert.Contains(status.Reports, report => report.StartsWith("Connecting", StringComparison.Ordinal));
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_AKeyLogin_IsRefusedBeforeAnySocketIsOpened()
	{
		FakeCredentialSource credentials = new(RdpHarness.User, null, AuthenticationMethod.PublicKey);
		ProtocolConnectContext context = RdpHarness.CreateContext(LoopbackPort.Free(), credentials);

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => RdpHarness.CreateProvider().ConnectAsync(context, TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.AuthenticationFailed, error.Failure);
		Assert.Equal(0, credentials.GetCount);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ACancelledPrompt_SaysTheLoginWasCancelled()
	{
		ProtocolConnectContext context = RdpHarness.CreateContext(LoopbackPort.Free(), new CancellingCredentialSource());

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => RdpHarness.CreateProvider().ConnectAsync(context, TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.Cancelled, error.Failure);
	}

	[Fact(Timeout = RdpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_AnAnonymousLogin_NeverAsksForASecret()
	{
		FakeCredentialSource credentials = new(null, null, AuthenticationMethod.Anonymous);
		ProtocolConnectContext context = RdpHarness.CreateContext(LoopbackPort.Free(), credentials);

		await Assert.ThrowsAsync<ProtocolConnectException>(
			() => RdpHarness.CreateProvider().ConnectAsync(context, TestContext.Current.CancellationToken));

		Assert.Equal(0, credentials.GetCount);
	}

	private sealed class CancellingCredentialSource : ICredentialSource
	{
		public string? Username => RdpHarness.User;

		public AuthenticationMethod Method => AuthenticationMethod.Password;

		public ValueTask<LoginCredentials?> GetAsync(CancellationToken cancellationToken) =>
			ValueTask.FromResult<LoginCredentials?>(null);

		public ValueTask<LoginCredentials?> RetryAsync(string reason, CancellationToken cancellationToken) =>
			ValueTask.FromResult<LoginCredentials?>(null);
	}

	/// <summary>A listener that is anything but an RDP server.</summary>
	private sealed class JunkListener : IDisposable
	{
		private readonly TcpListener _listener;
		private readonly CancellationTokenSource _stopping = new();

		private JunkListener(TcpListener listener) => _listener = listener;

		public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

		public static JunkListener Start(bool answer = true, bool closeAtOnce = false)
		{
			TcpListener listener = new(IPAddress.Loopback, 0);
			listener.Start();
			JunkListener junk = new(listener);
			_ = junk.AcceptAsync(answer, closeAtOnce);
			return junk;
		}

		public void Dispose()
		{
			_stopping.Cancel();
			_listener.Stop();
			_stopping.Dispose();
		}

		private async Task AcceptAsync(bool answer, bool closeAtOnce)
		{
			try
			{
				while (!_stopping.IsCancellationRequested)
				{
					using TcpClient client = await _listener.AcceptTcpClientAsync(_stopping.Token);
					if (closeAtOnce)
					{
						continue;
					}

					if (answer)
					{
						// A frame the length field of which the client can read, carrying a body that is not the
						// connection confirm RDP is waiting for. An answer with no length the client understands
						// (a web server's, say) leaves it waiting instead, which the timeout test covers.
						byte[] junk = [0x03, 0x00, 0x00, 0x07, 0x02, 0x00, 0x00];
						await client.GetStream().WriteAsync(junk, _stopping.Token);
						await client.GetStream().FlushAsync(_stopping.Token);
					}

					// Held open so the client waits on an answer that never comes.
					await Task.Delay(Timeout.InfiniteTimeSpan, _stopping.Token);
				}
			}
			catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or IOException)
			{
				// The test is over.
			}
		}
	}
}
