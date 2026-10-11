using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Tests.Fakes;
using static Mokaterm.UI.FileBrowser.Tests.Fakes.FakeFileSystem;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class PastePlanTests
{
	private static readonly HashSet<string> Existing = new(StringComparer.Ordinal) { "app.log", "conf", "notes.txt" };

	[Fact]
	public void Conflicts_AreTheTakenNamesOfEntriesFromElsewhere()
	{
		RemoteFileEntry[] sources = [File("/srv/a/app.log"), Folder("/srv/a/conf"), File("/srv/a/new.txt")];

		Assert.Equal(["app.log", "conf"], PastePlan.Conflicts(sources, "/srv/b", Existing));
	}

	// Replacing an entry with itself means nothing, so pasting into its own folder is never asked about.
	[Fact]
	public void Conflicts_LeaveOutAnEntryPastedIntoItsOwnFolder() =>
		Assert.Empty(PastePlan.Conflicts([File("/srv/b/notes.txt")], "/srv/b", Existing));

	[Fact]
	public void Build_KeepBoth_GivesTakenNamesACopyName()
	{
		RemoteFileEntry[] sources = [File("/srv/a/app.log"), Folder("/srv/a/conf"), File("/srv/a/new.txt")];

		List<PasteStep> steps = PastePlan.Build(sources, "/srv/b", Existing, PasteConflictChoice.KeepBoth);

		Assert.Equal(["/srv/b/app copy.log", "/srv/b/conf copy", "/srv/b/new.txt"], steps.Select(step => step.Destination));
		Assert.All(steps, step => Assert.False(step.Overwrite));
	}

	[Fact]
	public void Build_Overwrite_ReplacesTakenNamesAndKeepsTheRest()
	{
		RemoteFileEntry[] sources = [File("/srv/a/app.log"), File("/srv/a/new.txt")];

		List<PasteStep> steps = PastePlan.Build(sources, "/srv/b", Existing, PasteConflictChoice.Overwrite);

		Assert.Equal([("/srv/b/app.log", true), ("/srv/b/new.txt", false)], steps.Select(step => (step.Destination, step.Overwrite)));
	}

	[Fact]
	public void Build_Overwrite_StillCopiesAnEntryBesideItself()
	{
		List<PasteStep> steps = PastePlan.Build([File("/srv/b/notes.txt")], "/srv/b", Existing, PasteConflictChoice.Overwrite);

		PasteStep step = Assert.Single(steps);
		Assert.Equal("/srv/b/notes copy.txt", step.Destination);
		Assert.False(step.Overwrite);
	}

	// Duplicating two entries whose copies would take the same name counts the first copy as taken.
	[Fact]
	public void Build_CountsTheCopiesItAlreadyNamed()
	{
		HashSet<string> existing = new(StringComparer.Ordinal) { "a.txt", "a copy.txt" };
		RemoteFileEntry[] sources = [File("/srv/b/a.txt"), File("/srv/b/a copy.txt")];

		List<PasteStep> steps = PastePlan.Build(sources, "/srv/b", existing, PasteConflictChoice.KeepBoth);

		Assert.Equal(["/srv/b/a copy 2.txt", "/srv/b/a copy 3.txt"], steps.Select(step => step.Destination));
	}

	[Theory]
	[InlineData("/srv/a", "/srv/a", true)]
	[InlineData("/srv/a/inner", "/srv/a", true)]
	[InlineData("/srv/b", "/srv/a", false)]
	[InlineData("/srv/ab", "/srv/a", false)]
	public void IsIntoItself_OnlyForAFolderAndWhatIsInside(string target, string folder, bool expected) =>
		Assert.Equal(expected, PastePlan.IsIntoItself(Folder(folder), target));

	[Fact]
	public void IsIntoItself_NeverForAFileOrALink()
	{
		Assert.False(PastePlan.IsIntoItself(File("/srv/a"), "/srv/a/x"));
		Assert.False(PastePlan.IsIntoItself(Link("/srv/a", RemoteEntryKind.Directory), "/srv/a/x"));
	}

	[Fact]
	public async Task Copier_RunsEveryStepInOrder()
	{
		FakeFileSystem fileSystem = new FakeFileSystem().Contains("/srv/b", File("/srv/b/app.log"));
		List<PasteStep> steps = PastePlan.Build([File("/srv/a/app.log"), File("/srv/a/new.txt")], "/srv/b", Names("app.log"), PasteConflictChoice.Overwrite);

		await EntryCopier.CopyAsync(fileSystem, steps, new HashSet<int>(), TestContext.Current.CancellationToken);

		Assert.Equal(["cp -f /srv/a/app.log /srv/b/app.log", "cp /srv/a/new.txt /srv/b/new.txt"], fileSystem.Calls);
	}

	// A retry as root runs the same steps again; the copies made the first time would otherwise now stand in the way.
	[Fact]
	public async Task Copier_RunAgain_RepeatsOnlyWhatFailed()
	{
		FakeFileSystem fileSystem = new FakeFileSystem().Contains("/srv/b");
		List<PasteStep> steps = PastePlan.Build([File("/srv/a/one"), File("/srv/a/two"), File("/srv/a/three")], "/srv/b", Names(), PasteConflictChoice.KeepBoth);
		HashSet<int> done = [];
		fileSystem.CopyFailure = destination => destination == "/srv/b/two"
			? new RemoteFileSystemException(RemoteFileErrorKind.PermissionDenied, "Permission denied", destination)
			: null;

		await Assert.ThrowsAsync<RemoteFileSystemException>(() => EntryCopier.CopyAsync(fileSystem, steps, done, TestContext.Current.CancellationToken));
		Assert.Equal(["cp /srv/a/one /srv/b/one", "cp /srv/a/two /srv/b/two"], fileSystem.Calls);

		fileSystem.Calls.Clear();
		fileSystem.CopyFailure = null;
		await EntryCopier.CopyAsync(fileSystem, steps, done, TestContext.Current.CancellationToken);

		Assert.Equal(["cp /srv/a/two /srv/b/two", "cp /srv/a/three /srv/b/three"], fileSystem.Calls);
		Assert.Equal([0, 1, 2], done.Order());
	}

	private static HashSet<string> Names(params string[] names) => new(names, StringComparer.Ordinal);
}
