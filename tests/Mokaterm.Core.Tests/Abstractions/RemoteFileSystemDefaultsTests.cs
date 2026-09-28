using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>The default members of <see cref="IRemoteFileSystem"/>, as a file system that implements none of them gets them.</summary>
public sealed class RemoteFileSystemDefaultsTests
{
	[Fact]
	public async Task ListAccounts_IsEmpty()
	{
		IRemoteFileSystem fileSystem = new TreeFileSystem();

		Assert.True((await fileSystem.ListAccountsAsync(TestContext.Current.CancellationToken)).IsEmpty);
	}

	[Fact]
	public async Task WholeModeForEverything_IsOneRecursiveSetPermissions()
	{
		TreeFileSystem tree = new();

		await ((IRemoteFileSystem)tree).ChangePermissionsAsync(
			"/srv/site",
			new PermissionChange { Mode = Mode("750"), Recursive = true },
			TestContext.Current.CancellationToken);

		Assert.Equal(["chmod -R 750 /srv/site"], tree.Changes);
	}

	[Fact]
	public async Task FilesOnly_ChangesEachFileAndLeavesFoldersAndLinks()
	{
		TreeFileSystem tree = new();

		await ((IRemoteFileSystem)tree).ChangePermissionsAsync(
			"/srv/site",
			new PermissionChange { Mode = Mode("640"), Recursive = true, Targets = PermissionTargets.Files },
			TestContext.Current.CancellationToken);

		Assert.Equal(["chmod 640 /srv/site/img/a.png", "chmod 640 /srv/site/index.html"], tree.Changes.Order(StringComparer.Ordinal));
	}

	[Fact]
	public async Task PartialChange_StartsFromEachEntrysMode()
	{
		TreeFileSystem tree = new();

		await ((IRemoteFileSystem)tree).ChangePermissionsAsync(
			"/srv/site",
			new PermissionChange { Mode = UnixFileMode.GroupWrite, Mask = UnixFileMode.GroupWrite, Recursive = true },
			TestContext.Current.CancellationToken);

		Assert.Equal(
			["chmod 620 /srv/site/img/a.png", "chmod 664 /srv/site/index.html", "chmod 775 /srv/site", "chmod 775 /srv/site/img"],
			tree.Changes.Order(StringComparer.Ordinal));
	}

	[Fact]
	public async Task ConditionalExecute_KeepsPlainFilesNotExecutable()
	{
		TreeFileSystem tree = new();

		await ((IRemoteFileSystem)tree).ChangePermissionsAsync(
			"/srv/site",
			new PermissionChange { Mode = Mode("755"), Recursive = true, ConditionalExecute = true },
			TestContext.Current.CancellationToken);

		Assert.Contains("chmod 644 /srv/site/index.html", tree.Changes);
		Assert.Contains("chmod 755 /srv/site/img", tree.Changes);
	}

	[Fact]
	public async Task LinkGivenDirectly_ChangesItsTargetStartingFromTheTargetsMode()
	{
		TreeFileSystem tree = new();

		await ((IRemoteFileSystem)tree).ChangePermissionsAsync(
			"/srv/current",
			new PermissionChange { Mode = UnixFileMode.None, Mask = UnixFileMode.OtherRead | UnixFileMode.OtherExecute },
			TestContext.Current.CancellationToken);

		Assert.Equal(["chmod 750 /srv/current"], tree.Changes);
	}

	[Fact]
	public async Task UnreportedMode_CannotBeChangedInPart()
	{
		TreeFileSystem tree = new();

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(async () =>
			await ((IRemoteFileSystem)tree).ChangePermissionsAsync(
				"/srv/mystery",
				new PermissionChange { Mode = UnixFileMode.GroupWrite, Mask = UnixFileMode.GroupWrite },
				TestContext.Current.CancellationToken));

		Assert.Equal(RemoteFileErrorKind.NotSupported, failure.Kind);
		Assert.Empty(tree.Changes);
	}

	[Fact]
	public async Task SetGroup_KeepsEveryOwner()
	{
		TreeFileSystem tree = new();

		await ((IRemoteFileSystem)tree).SetGroupAsync("/srv/site", "www", recursive: true, TestContext.Current.CancellationToken);

		Assert.Equal(
			["chown abc:www /srv/site/img/a.png", "chown root:www /srv/site", "chown root:www /srv/site/img", "chown www-data:www /srv/site/index.html"],
			tree.Changes.Order(StringComparer.Ordinal));
	}

	[Fact]
	public async Task MissingEntry_IsNotFound()
	{
		TreeFileSystem tree = new();

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(async () =>
			await ((IRemoteFileSystem)tree).SetGroupAsync("/srv/gone", "www", recursive: false, TestContext.Current.CancellationToken));

		Assert.Equal(RemoteFileErrorKind.NotFound, failure.Kind);
	}

	private static UnixFileMode Mode(string octal) => (UnixFileMode)Convert.ToInt32(octal, 8);

	/// <summary>A small fixed tree that implements only the members without a default, and records what it changes.</summary>
	private sealed class TreeFileSystem : IRemoteFileSystem
	{
		private static readonly RemoteFileEntry[] Entries =
		[
			Entry("/srv/site", RemoteEntryKind.Directory, "755", "root"),
			Entry("/srv/site/index.html", RemoteEntryKind.File, "644", "www-data"),
			Entry("/srv/site/img", RemoteEntryKind.Directory, "755", "root"),
			Entry("/srv/site/img/a.png", RemoteEntryKind.File, "600", "abc"),
			Entry("/srv/site/latest", RemoteEntryKind.SymbolicLink, "777", "root"),
			Entry("/srv/current", RemoteEntryKind.SymbolicLink, "777", "root"),
			Entry("/srv/mystery", RemoteEntryKind.File, null, null),
		];

		public List<string> Changes { get; } = [];

		public RemoteFileSystemFeatures Features => RemoteFileSystemFeatures.Permissions | RemoteFileSystemFeatures.Ownership;

		public string UserName => "abc";

		public bool IsElevated => false;

		public ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("/home/abc");

		public ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(path == "/srv/current" ? "/srv/site" : path);

		public ValueTask<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<IReadOnlyList<RemoteFileEntry>>([.. Entries.Where(entry => RemotePath.GetParent(entry.Path) == path)]);

		public ValueTask<RemoteFileEntry?> StatAsync(string path, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(Entries.FirstOrDefault(entry => entry.Path == path));

		public ValueTask SetPermissionsAsync(string path, UnixFileMode permissions, bool recursive, CancellationToken cancellationToken = default)
		{
			Changes.Add($"chmod{(recursive ? " -R" : "")} {UnixFileModeFormat.ToOctal(permissions)} {path}");
			return ValueTask.CompletedTask;
		}

		public ValueTask SetOwnerAsync(string path, string owner, string? group, bool recursive, CancellationToken cancellationToken = default)
		{
			Changes.Add($"chown{(recursive ? " -R" : "")} {owner}{(group is null ? "" : ":" + group)} {path}");
			return ValueTask.CompletedTask;
		}

		public ValueTask CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask RenameAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask UploadAsync(string path, Stream source, UploadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask DownloadAsync(string path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;

		private static RemoteFileEntry Entry(string path, RemoteEntryKind kind, string? mode, string? owner) => new()
		{
			Name = RemotePath.GetName(path),
			Path = path,
			Kind = kind,
			Permissions = mode is null ? null : Mode(mode),
			Owner = owner,
			Group = owner,
		};
	}
}
