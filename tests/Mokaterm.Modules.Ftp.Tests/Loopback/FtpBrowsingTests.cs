using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ftp.Tests.Fakes;

namespace Mokaterm.Modules.Ftp.Tests.Loopback;

public sealed class FtpBrowsingTests
{
	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task List_PassiveReplyNamingAnotherHost_ConnectsToTheServerItself()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FakeSettingsService settings = new();
		settings.Set(new FtpSettings { ConnectTimeoutSeconds = 3 });

		// 192.0.2.1 is a documentation address with nothing behind it: a client that follows the reply lists nothing.
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { PassiveAddress = "192,0,2,1" });
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken, FtpHarness.CreateProvider(settings));
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		IReadOnlyList<RemoteFileEntry> entries = await files.ListAsync(FtpHarness.Home, cancellationToken);

		Assert.Contains(entries, entry => entry.Name == "notes.txt");
		Assert.Contains("PASV", server.Commands);
	}

	[Theory(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	[InlineData(false)]
	[InlineData(true)]
	public async Task List_Home_IncludesHiddenFilesAndResolvesLinks(bool machineListings)
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { MachineListings = machineListings });
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		IReadOnlyList<RemoteFileEntry> entries = await files.ListAsync(FtpHarness.Home, cancellationToken);

		Assert.Equal([".profile", "notes.txt", "readme", "site", "www"], entries.Select(entry => entry.Name).Order(StringComparer.Ordinal));
		RemoteFileEntry notes = Assert.Single(entries, entry => entry.Name == "notes.txt");
		Assert.Equal(FtpHarness.NotesPath, notes.Path);
		Assert.Equal(RemoteEntryKind.File, notes.Kind);
		Assert.Equal(FtpHarness.NotesText.Length, notes.Size);
		Assert.Equal(FakeFileTree.Mode("644"), notes.Permissions);
		Assert.NotNull(notes.LastModified);
		Assert.Equal(FakeFileTree.DefaultModified.Date, notes.LastModified.Value.UtcDateTime.Date);

		RemoteFileEntry site = Assert.Single(entries, entry => entry.Name == "site");
		Assert.Equal(RemoteEntryKind.SymbolicLink, site.Kind);
		Assert.Equal(RemoteEntryKind.Directory, site.LinkTargetKind);
		Assert.True(site.IsDirectoryLike);
		Assert.Equal(RemoteEntryKind.File, Assert.Single(entries, entry => entry.Name == "readme").LinkTargetKind);

		Assert.True(files.Features.HasFlag(RemoteFileSystemFeatures.SymbolicLinks));
		Assert.True(files.Features.HasFlag(RemoteFileSystemFeatures.Permissions));
		Assert.False(files.Features.HasFlag(RemoteFileSystemFeatures.Ownership));
		Assert.False(files.Features.HasFlag(RemoteFileSystemFeatures.Elevation));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task List_ServerWithoutLsOptions_FallsBackToPlainListing()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { ListAllFilesSupported = false });
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		IReadOnlyList<RemoteFileEntry> first = await files.ListAsync(FtpHarness.Home, cancellationToken);
		IReadOnlyList<RemoteFileEntry> second = await files.ListAsync(FtpHarness.Home, cancellationToken);

		Assert.Contains(first, entry => entry.Name == "notes.txt");
		Assert.Equal(first.Count, second.Count);
		Assert.Single(server.Commands, command => command.StartsWith("LIST -a", StringComparison.Ordinal));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task List_EmptyFolder_ReturnsNothing()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		server.Files.AddDirectory("/home/alice/empty");
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		Assert.Empty(await files.ListAsync("/home/alice/empty", cancellationToken));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task List_MissingFolder_ThrowsNotFound()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		RemoteFileSystemException exception = await Assert.ThrowsAsync<RemoteFileSystemException>(() => files.ListAsync("/home/alice/nope", cancellationToken).AsTask());

		Assert.Equal(RemoteFileErrorKind.NotFound, exception.Kind);
		Assert.Equal("/home/alice/nope", exception.Path);
	}

	[Theory(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Stat_FindsFilesFoldersAndHiddenEntries(bool machineListings)
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { MachineListings = machineListings });
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		RemoteFileEntry? notes = await files.StatAsync(FtpHarness.NotesPath, cancellationToken);
		RemoteFileEntry? www = await files.StatAsync("/home/alice/www/", cancellationToken);
		RemoteFileEntry? profile = await files.StatAsync("/home/alice/.profile", cancellationToken);
		RemoteFileEntry? root = await files.StatAsync("/", cancellationToken);

		Assert.Equal(RemoteEntryKind.File, notes?.Kind);
		Assert.Equal(FtpHarness.NotesText.Length, notes?.Size);
		Assert.Equal("notes.txt", notes?.Name);
		Assert.Equal(RemoteEntryKind.Directory, www?.Kind);
		Assert.Equal("/home/alice/www", www?.Path);
		Assert.NotNull(profile);
		Assert.Equal(RemoteEntryKind.Directory, root?.Kind);
		Assert.Null(await files.StatAsync("/home/alice/missing.txt", cancellationToken));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task ResolvePath_HandlesHomeRelativeAndParentSegments()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		Assert.Equal(FtpHarness.Home, await files.ResolvePathAsync("~", cancellationToken));
		Assert.Equal("/home/alice/www", await files.ResolvePathAsync("~/www/../www/.", cancellationToken));
		Assert.Equal("/home/bob", await files.ResolvePathAsync("../bob", cancellationToken));
		Assert.Equal(FtpHarness.NotesPath, await files.ResolvePathAsync("notes.txt", cancellationToken));
		Assert.Equal("/", await files.ResolvePathAsync("/..", cancellationToken));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task InitialDirectoryOption_SetsTheHomeFolderButNotTilde()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		ProtocolOptions options = new FtpConnectionOptions { InitialDirectory = " www " }.ApplyTo(ProtocolOptions.Empty);
		await using IProtocolSession session = await FtpHarness.CreateProvider().ConnectAsync(FtpHarness.CreateContext(server.Port, options: options), cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		Assert.Equal("/home/alice/www", await files.GetHomeDirectoryAsync(cancellationToken));
		Assert.Equal(FtpHarness.Home, await files.ResolvePathAsync("~", cancellationToken));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task CreateRenameAndDelete_FollowTheContract()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		server.Files.AddFile("/home/alice/other.txt", "other");
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		await files.CreateDirectoryAsync("/home/alice/archive", cancellationToken);
		Assert.True(server.Files.IsDirectory("/home/alice/archive"));
		Assert.Equal(RemoteFileErrorKind.AlreadyExists, (await Assert.ThrowsAsync<RemoteFileSystemException>(() => files.CreateDirectoryAsync("/home/alice/archive", cancellationToken).AsTask())).Kind);

		await files.RenameAsync(FtpHarness.NotesPath, "/home/alice/archive/notes.txt", overwrite: false, cancellationToken);
		Assert.Null(server.Files.Get(FtpHarness.NotesPath));
		Assert.NotNull(server.Files.Get("/home/alice/archive/notes.txt"));

		RemoteFileSystemException exists = await Assert.ThrowsAsync<RemoteFileSystemException>(() => files.RenameAsync("/home/alice/other.txt", "/home/alice/archive/notes.txt", overwrite: false, cancellationToken).AsTask());
		Assert.Equal(RemoteFileErrorKind.AlreadyExists, exists.Kind);
		await files.RenameAsync("/home/alice/other.txt", "/home/alice/archive/notes.txt", overwrite: true, cancellationToken);
		Assert.Equal("other"u8.ToArray(), server.Files.Get("/home/alice/archive/notes.txt")?.Content);
		Assert.Null(server.Files.Get("/home/alice/other.txt"));

		RemoteFileSystemException notEmpty = await Assert.ThrowsAsync<RemoteFileSystemException>(() => files.DeleteAsync("/home/alice/archive", recursive: false, cancellationToken).AsTask());
		Assert.Equal(RemoteFileErrorKind.DirectoryNotEmpty, notEmpty.Kind);

		await files.DeleteAsync("/home/alice/archive", recursive: true, cancellationToken);
		Assert.Null(server.Files.Get("/home/alice/archive"));
		Assert.Null(await files.StatAsync("/home/alice/archive", cancellationToken));

		RemoteFileSystemException missing = await Assert.ThrowsAsync<RemoteFileSystemException>(() => files.DeleteAsync("/home/alice/archive", recursive: false, cancellationToken).AsTask());
		Assert.Equal(RemoteFileErrorKind.NotFound, missing.Kind);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Rename_FolderIntoItself_IsRefusedWithoutAskingTheServer()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		await Assert.ThrowsAsync<RemoteFileSystemException>(() => files.RenameAsync("/home/alice/www", "/home/alice/www/inner", overwrite: false, cancellationToken).AsTask());

		Assert.DoesNotContain(server.Commands, command => command.StartsWith("RNFR", StringComparison.Ordinal));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task DeleteRecursive_RemovesLinksWithoutFollowingThem()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		server.Files
			.AddFile("/home/alice/tree/sub/deep/a.txt", "a")
			.AddFile("/home/alice/tree/.hidden", "h")
			.AddLink("/home/alice/tree/www-link", "../www");
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		await files.DeleteAsync("/home/alice/tree", recursive: true, cancellationToken);

		Assert.Null(server.Files.Get("/home/alice/tree"));
		Assert.Null(server.Files.Get("/home/alice/tree/www-link"));
		Assert.NotNull(server.Files.Get("/home/alice/www/index.html"));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task SetPermissions_ChangesModesRecursivelyButSkipsLinks()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		server.Files.AddLink("/home/alice/www/outside", "../notes.txt");
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		await files.SetPermissionsAsync(FtpHarness.NotesPath, FakeFileTree.Mode("600"), recursive: false, cancellationToken);
		await files.SetPermissionsAsync("/home/alice/www", FakeFileTree.Mode("2750"), recursive: true, cancellationToken);

		Assert.Equal(FakeFileTree.Mode("600"), server.Files.Get(FtpHarness.NotesPath)?.Mode);
		Assert.Equal(FakeFileTree.Mode("2750"), server.Files.Get("/home/alice/www")?.Mode);
		Assert.Equal(FakeFileTree.Mode("2750"), server.Files.Get("/home/alice/www/index.html")?.Mode);
		Assert.Equal(FakeFileTree.Mode("777"), server.Files.Get("/home/alice/www/outside")?.Mode);
		Assert.Contains(server.Commands, command => command == "SITE CHMOD 2750 /home/alice/www");
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task SetPermissions_ServerWithoutSiteChmod_ReportsNotSupportedAndDropsTheFeature()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { SupportsChmod = false });
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);
		Assert.True(files.Features.HasFlag(RemoteFileSystemFeatures.Permissions));

		RemoteFileSystemException first = await Assert.ThrowsAsync<RemoteFileSystemException>(() => files.SetPermissionsAsync(FtpHarness.NotesPath, FakeFileTree.Mode("600"), recursive: false, cancellationToken).AsTask());
		RemoteFileSystemException second = await Assert.ThrowsAsync<RemoteFileSystemException>(() => files.SetPermissionsAsync(FtpHarness.NotesPath, FakeFileTree.Mode("600"), recursive: false, cancellationToken).AsTask());

		Assert.Equal(RemoteFileErrorKind.NotSupported, first.Kind);
		Assert.Equal(RemoteFileErrorKind.NotSupported, second.Kind);
		Assert.False(files.Features.HasFlag(RemoteFileSystemFeatures.Permissions));
		Assert.Single(server.Commands, command => command.StartsWith("SITE", StringComparison.Ordinal));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task SetOwner_IsNotSupported()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		RemoteFileSystemException exception = await Assert.ThrowsAsync<RemoteFileSystemException>(() => files.SetOwnerAsync(FtpHarness.NotesPath, "root", "root", recursive: false, cancellationToken).AsTask());

		Assert.Equal(RemoteFileErrorKind.NotSupported, exception.Kind);
	}
}
