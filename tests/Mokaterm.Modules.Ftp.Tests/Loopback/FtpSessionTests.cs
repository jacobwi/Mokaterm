using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Ftp.Tests.Fakes;

namespace Mokaterm.Modules.Ftp.Tests.Loopback;

public sealed class FtpSessionTests
{
	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Session_ExposesOnlyTheFileSystemFeature()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);

		IFileSystemFeature feature = FtpHarness.FileSystemFeature(session);

		Assert.Null(session.GetFeature<ITerminalChannel>());
		Assert.Null(session.GetFeature<IProtocolSession>());
		Assert.False(feature.SupportsElevation);
		await Assert.ThrowsAsync<NotSupportedException>(async () => await feature.OpenElevatedAsync(cancellationToken));
		Assert.Same(await feature.OpenAsync(cancellationToken), await feature.OpenAsync(cancellationToken));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task FileSystem_DisposedByTheBrowser_KeepsWorkingForTheSession()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);

		await using (IRemoteFileSystem first = await FtpHarness.OpenFileSystemAsync(session, cancellationToken))
		{
			Assert.NotEmpty(await first.ListAsync(FtpHarness.Home, cancellationToken));
		}

		IRemoteFileSystem again = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);
		Assert.NotEmpty(await again.ListAsync(FtpHarness.Home, cancellationToken));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Dispose_CompletesTheSessionAndClosesEveryConnection()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IFileSystemFeature feature = FtpHarness.FileSystemFeature(session);
		IRemoteFileSystem files = await feature.OpenAsync(cancellationToken);
		await files.DownloadAsync(FtpHarness.NotesPath, new MemoryStream(), null, cancellationToken);
		Assert.Equal(2, server.OpenConnections);

		await session.DisposeAsync();

		Assert.True(session.Completion.IsCompletedSuccessfully);
		await Eventually.TrueAsync(() => server.OpenConnections == 0, "the server sees the connection close");
		Assert.Contains("QUIT", server.Commands);
		RemoteFileSystemException closed = await Assert.ThrowsAsync<RemoteFileSystemException>(() => files.ListAsync(FtpHarness.Home, cancellationToken).AsTask());
		Assert.Equal(RemoteFileErrorKind.ConnectionLost, closed.Kind);
		await Assert.ThrowsAsync<RemoteFileSystemException>(async () => await feature.OpenAsync(cancellationToken));
		await session.DisposeAsync();
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Dispose_ServerThatStopsAnswering_DoesNotWaitForItsReplies()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FakeSettingsService settings = new();
		settings.Set(new FtpSettings { DataTimeoutSeconds = 20 });
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { IgnoreQuit = true });
		IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken, FtpHarness.CreateProvider(settings));
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		// The download leaves a transfer connection idle in the pool beside the browsing connection.
		await files.DownloadAsync(FtpHarness.NotesPath, new MemoryStream(), null, cancellationToken);
		Assert.Equal(2, server.OpenConnections);

		Stopwatch closing = Stopwatch.StartNew();
		await session.DisposeAsync();
		closing.Stop();

		Assert.True(closing.Elapsed < TimeSpan.FromSeconds(10), $"Closing the session waited {closing.Elapsed} for a server that stopped answering.");
		Assert.True(session.Completion.IsCompletedSuccessfully);
		Assert.Contains("QUIT", server.Commands);
		await Eventually.TrueAsync(() => server.OpenConnections == 0, "the server sees the connection close");
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task KeepAlive_SendsNoopWhenIdle()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FakeTimeProvider time = new();
		FakeSettingsService settings = new();
		settings.Set(new FtpSettings { KeepAliveSeconds = 5 });
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken, FtpHarness.CreateProvider(settings, time));

		time.Advance(TimeSpan.FromSeconds(5));

		await Eventually.TrueAsync(() => server.Commands.Contains("NOOP"), "the idle connection sends NOOP");
		Assert.False(session.Completion.IsCompleted);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task KeepAlive_ServerGone_FaultsCompletionAfterThreeFailures()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FakeTimeProvider time = new();
		FakeSettingsService settings = new();
		settings.Set(new FtpSettings { KeepAliveSeconds = 5, ConnectTimeoutSeconds = 1, DataTimeoutSeconds = 1 });
		LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken, FtpHarness.CreateProvider(settings, time));

		await server.DisposeAsync();

		// Each failed NOOP includes a real reconnect attempt, which Windows stretches to the connect timeout for a closed
		// port, so keep ticking against a wall-clock deadline instead of a fixed number of ticks.
		Stopwatch elapsed = Stopwatch.StartNew();
		while (!session.Completion.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(30))
		{
			time.Advance(TimeSpan.FromSeconds(5));
			await Task.Delay(100, cancellationToken);
		}

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(() => session.Completion.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken));
		Assert.Equal(RemoteFileErrorKind.ConnectionLost, failure.Kind);
		Assert.NotNull(failure.InnerException);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task KeepAlive_Off_SendsNothing()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FakeTimeProvider time = new();
		FakeSettingsService settings = new();
		settings.Set(new FtpSettings { KeepAliveSeconds = 0 });
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken, FtpHarness.CreateProvider(settings, time));

		time.Advance(TimeSpan.FromMinutes(10));
		await Task.Delay(200, cancellationToken);

		Assert.DoesNotContain("NOOP", server.Commands);
	}
}
