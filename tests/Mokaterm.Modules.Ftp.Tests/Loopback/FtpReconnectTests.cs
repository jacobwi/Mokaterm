using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ftp.FileSystem;
using Mokaterm.Modules.Ftp.Tests.Fakes;

namespace Mokaterm.Modules.Ftp.Tests.Loopback;

public sealed class FtpReconnectTests
{
	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task DroppedConnection_ReconnectsOnTheNextOperation()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);
		Assert.NotEmpty(await files.ListAsync(FtpHarness.Home, cancellationToken));

		server.DropConnections();
		await Eventually.TrueAsync(() => server.OpenConnections == 0, "the server sees the connection close");

		Assert.NotEmpty(await files.ListAsync(FtpHarness.Home, cancellationToken));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task DroppedConnection_AfterTheLastEnteredFolderWasDeleted_StillReconnects()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		server.Files.AddDirectory("/home/alice/scratch");
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		// Listing an empty folder enters it to tell "empty" from "missing".
		Assert.Empty(await files.ListAsync("/home/alice/scratch", cancellationToken));
		server.Files.Remove("/home/alice/scratch");
		server.DropConnections();
		await Eventually.TrueAsync(() => server.OpenConnections == 0, "the server sees the connection close");

		Assert.NotEmpty(await files.ListAsync(FtpHarness.Home, cancellationToken));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task DroppedIdleConnection_IsNoticedBeforeAChangeIsSent()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FakeTimeProvider time = new();
		FakeSettingsService settings = new();
		settings.Set(new FtpSettings { KeepAliveSeconds = 0 });
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken, FtpHarness.CreateProvider(settings, time));
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		server.DropConnections();
		await Eventually.TrueAsync(() => server.OpenConnections == 0, "the server sees the connection close");
		time.Advance(FtpClientPool.ProbeAfterIdle);
		await files.RenameAsync(FtpHarness.NotesPath, "/home/alice/renamed.txt", overwrite: false, cancellationToken);

		Assert.NotNull(server.Files.Get("/home/alice/renamed.txt"));
		Assert.Single(server.Commands, command => command.StartsWith("RNTO", StringComparison.Ordinal));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task DroppedBusyConnection_ChangeFailsOnceAndIsNotRepeated()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);
		Assert.NotEmpty(await files.ListAsync(FtpHarness.Home, cancellationToken));

		server.DropConnections();
		await Eventually.TrueAsync(() => server.OpenConnections == 0, "the server sees the connection close");
		RemoteFileSystemException exception = await Assert.ThrowsAsync<RemoteFileSystemException>(() =>
			files.RenameAsync(FtpHarness.NotesPath, "/home/alice/renamed.txt", overwrite: false, cancellationToken).AsTask());

		Assert.Equal(RemoteFileErrorKind.ConnectionLost, exception.Kind);
		Assert.DoesNotContain(server.Commands, command => command.StartsWith("RNFR", StringComparison.Ordinal));
		Assert.NotEmpty(await files.ListAsync(FtpHarness.Home, cancellationToken));
		Assert.NotNull(server.Files.Get(FtpHarness.NotesPath));
	}
}
