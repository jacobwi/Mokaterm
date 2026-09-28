using System.Net;
using System.Net.Sockets;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ftp.Tests.Fakes;

namespace Mokaterm.Modules.Ftp.Tests.Loopback;

public sealed class FtpConnectTests
{
	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_WrongPassword_AsksAgainThroughTheCredentialSource()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		FakeCredentialSource credentials = new(FtpHarness.User, "wrong", AuthenticationMethod.Password, FtpHarness.Password);
		RecordingProgress<string> status = new();

		await using IProtocolSession session = await FtpHarness.CreateProvider().ConnectAsync(FtpHarness.CreateContext(server.Port, credentials, status: status), cancellationToken);

		Assert.Equal("The server rejected the login: Login incorrect.", Assert.Single(credentials.RetryReasons));
		Assert.Contains("Connecting", status.Reports);
		Assert.Equal(FtpHarness.User, (await FtpHarness.OpenFileSystemAsync(session, cancellationToken)).UserName);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_WithoutAStatusSink_StillConnects()
	{
		// ProtocolConnectContext.Status is optional, and the shell leaves it out when nothing is watching a session
		// come up, so a provider that reports its progress must not need one.
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		ProtocolConnectContext context = FtpHarness.CreateContext(server.Port, withoutStatus: true);

		Assert.Null(context.Status);

		await using IProtocolSession session = await FtpHarness.CreateProvider().ConnectAsync(context, cancellationToken);

		Assert.Equal(FtpHarness.User, (await FtpHarness.OpenFileSystemAsync(session, cancellationToken)).UserName);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_WhenLoginAttemptsRunOut_FailsWithAuthenticationFailed()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		FakeSettingsService settings = new();
		settings.Set(new FtpSettings { AuthenticationAttempts = 2 });
		FakeCredentialSource credentials = new(FtpHarness.User, "wrong", AuthenticationMethod.Password, "still wrong", FtpHarness.Password);

		ProtocolConnectException exception = await Assert.ThrowsAsync<ProtocolConnectException>(() =>
			FtpHarness.CreateProvider(settings).ConnectAsync(FtpHarness.CreateContext(server.Port, credentials), cancellationToken));

		Assert.Equal(ConnectFailure.AuthenticationFailed, exception.Failure);
		Assert.Single(credentials.RetryReasons);
		await Eventually.TrueAsync(() => server.OpenConnections == 0, "the server sees the connection close");
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_WhenTheUserGivesUp_FailsWithAuthenticationFailed()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		FakeCredentialSource credentials = new(FtpHarness.User, "wrong");

		ProtocolConnectException exception = await Assert.ThrowsAsync<ProtocolConnectException>(() =>
			FtpHarness.CreateProvider().ConnectAsync(FtpHarness.CreateContext(server.Port, credentials), cancellationToken));

		Assert.Equal(ConnectFailure.AuthenticationFailed, exception.Failure);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_Anonymous_LogsInWithoutAskingForCredentials()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { AllowAnonymous = true });
		FakeCredentialSource credentials = new(null, null, AuthenticationMethod.Anonymous);

		await using IProtocolSession session = await FtpHarness.CreateProvider().ConnectAsync(FtpHarness.CreateContext(server.Port, credentials), cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		Assert.Equal(0, credentials.GetCount);
		Assert.Equal("anonymous", files.UserName);
		Assert.Equal("/pub", await files.GetHomeDirectoryAsync(cancellationToken));
		Assert.Contains(server.Commands, command => command == "USER anonymous");
		Assert.Contains(await files.ListAsync("/pub", cancellationToken), entry => entry.Name == "welcome.txt");
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_KeyCredentials_AreRefused()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		FakeCredentialSource credentials = new(FtpHarness.User, null, AuthenticationMethod.PublicKey);

		ProtocolConnectException exception = await Assert.ThrowsAsync<ProtocolConnectException>(() =>
			FtpHarness.CreateProvider().ConnectAsync(FtpHarness.CreateContext(server.Port, credentials), cancellationToken));

		Assert.Equal(ConnectFailure.AuthenticationFailed, exception.Failure);
		Assert.Equal(0, server.TotalConnections);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_NothingListening_FailsWithHostUnreachable()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();

		ProtocolConnectException exception = await Assert.ThrowsAsync<ProtocolConnectException>(() =>
			FtpHarness.CreateProvider().ConnectAsync(FtpHarness.CreateContext(port), cancellationToken));

		Assert.Equal(ConnectFailure.HostUnreachable, exception.Failure);
		Assert.Contains($"127.0.0.1:{port}", exception.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ServerFull_FailsWithProtocolErrorShowingTheReply()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { MaxConnections = 0 });

		ProtocolConnectException exception = await Assert.ThrowsAsync<ProtocolConnectException>(() =>
			FtpHarness.CreateProvider().ConnectAsync(FtpHarness.CreateContext(server.Port), cancellationToken));

		Assert.Equal(ConnectFailure.ProtocolError, exception.Failure);
		Assert.Contains("421", exception.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_Cancelled_ThrowsCancellation()
	{
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		await cancel.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FtpHarness.CreateProvider().ConnectAsync(FtpHarness.CreateContext(server.Port), cancel.Token));
	}
}
