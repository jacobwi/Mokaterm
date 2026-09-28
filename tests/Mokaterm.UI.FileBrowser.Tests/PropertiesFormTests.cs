using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Properties;
using static Mokaterm.UI.FileBrowser.Tests.Fakes.FakeFileSystem;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class PropertiesFormTests
{
	private const RemoteFileSystemFeatures Editable = RemoteFileSystemFeatures.Permissions | RemoteFileSystemFeatures.Ownership;

	[Fact]
	public void NothingEdited_IsNoChange()
	{
		PropertiesForm form = Form(File("/srv/a.txt"));

		Assert.False(form.HasChanges);
		Assert.Null(form.BuildChange());
	}

	[Fact]
	public void NewOwner_IsAllThatIsApplied()
	{
		PropertiesForm form = Form(File("/srv/a.txt"));

		form.Owner.Text = "www-data";
		PropertiesChange change = Change(form);

		Assert.Equal("www-data", change.Owner);
		Assert.Null(change.Group);
		Assert.Null(change.Permissions);
		Assert.False(change.OwnershipRecursive);
		Assert.False(change.AsRoot);
	}

	[Fact]
	public void NewOwner_NeedsRootUntilRootIsChosen()
	{
		PropertiesForm form = Form(File("/srv/a.txt"));
		form.Owner.Text = "www-data";

		Assert.True(form.NeedsRootForOwner);

		form.AsRoot = true;
		Assert.False(form.NeedsRootForOwner);
		Assert.True(Change(form).AsRoot);
	}

	[Fact]
	public void ChangedBit_OnOneEntry_IsAWholeMode()
	{
		PropertiesForm form = Form(File("/srv/a.txt", mode: Mode("644")));

		form.Mode.Set(UnixFileMode.UserExecute, true);
		PropertiesChange change = Change(form);

		Assert.Null(change.Owner);
		Assert.Equal(new PermissionChange { Mode = Mode("744") }, change.Permissions);
	}

	[Fact]
	public void InvalidField_BlocksEveryChange()
	{
		PropertiesForm form = Form(File("/srv/a.txt"));
		form.Mode.Set(UnixFileMode.OtherWrite, true);

		form.Group.Text = "two words";

		Assert.False(form.IsValid);
		Assert.Null(form.BuildChange());
	}

	[Fact]
	public void ApplyingInside_PushesUnchangedValuesDown()
	{
		PropertiesForm form = Form(Folder("/srv/site", owner: "www-data", group: "www-data"));

		form.OwnershipRecursive = true;
		PropertiesChange ownership = Change(form);

		Assert.Equal("www-data", ownership.Owner);
		Assert.Equal("www-data", ownership.Group);
		Assert.True(ownership.OwnershipRecursive);
		Assert.Null(ownership.Permissions);

		form.OwnershipRecursive = false;
		form.PermissionsRecursive = true;
		form.Targets = PermissionTargets.Files;
		form.ConditionalExecute = true;
		PropertiesChange permissions = Change(form);

		Assert.Null(permissions.Owner);
		Assert.Equal(
			new PermissionChange { Mode = Mode("755"), Recursive = true, Targets = PermissionTargets.Files, ConditionalExecute = true },
			permissions.Permissions);
	}

	[Fact]
	public void ApplyingInside_NeedsAFolder()
	{
		PropertiesForm form = Form(File("/srv/a.txt"));

		form.OwnershipRecursive = true;
		form.PermissionsRecursive = true;

		Assert.False(form.HasFolders);
		Assert.Null(form.BuildChange());
	}

	[Fact]
	public void TargetsAndConditionalExecute_OnlyCountWhileApplyingInside()
	{
		PropertiesForm form = Form(Folder("/srv/site"));
		form.Targets = PermissionTargets.Files;
		form.ConditionalExecute = true;

		form.Mode.Set(UnixFileMode.GroupWrite, true);

		Assert.Equal(new PermissionChange { Mode = Mode("775") }, Change(form).Permissions);
	}

	[Fact]
	public void FoldersOnly_IgnoresConditionalExecute()
	{
		PropertiesForm form = Form(Folder("/srv/site"));
		form.PermissionsRecursive = true;
		form.Targets = PermissionTargets.Folders;

		form.ConditionalExecute = true;

		Assert.False(Change(form).Permissions?.ConditionalExecute);
	}

	[Fact]
	public void MixedSelection_OnlyChangesTheBitsSet()
	{
		PropertiesForm form = Form(
			File("/srv/run.sh", mode: Mode("755"), owner: "root"),
			File("/srv/app.conf", mode: Mode("640"), owner: "abc"));

		Assert.True(form.Owner.IsMixed);
		Assert.True(form.Mode.WasMixed);
		Assert.False(form.HasChanges);

		form.Mode.Set(UnixFileMode.OtherRead, false);
		PropertiesChange change = Change(form);
		PermissionChange permissions = Assert.IsType<PermissionChange>(change.Permissions);

		Assert.Null(change.Owner);
		Assert.Null(change.Group);
		Assert.Equal(Mode("751"), permissions.Apply(Mode("755"), isDirectory: false));
		Assert.Equal(Mode("640"), permissions.Apply(Mode("640"), isDirectory: false));
	}

	[Fact]
	public void Links_AreLeftOut()
	{
		PropertiesForm links = Form(Link("/srv/current"));

		Assert.True(links.IsReadOnly);
		Assert.Equal(1, links.SkippedLinks);
		Assert.Null(links.BuildChange());

		// The link's own 777 would otherwise make the file's mode look mixed.
		PropertiesForm mixed = Form(Link("/srv/current"), File("/srv/a.txt", mode: Mode("600")));
		Assert.False(mixed.Mode.WasMixed);
		Assert.Equal("600", mixed.Mode.OctalText);
	}

	[Fact]
	public void MissingFeatures_LeaveTheirPartsOut()
	{
		PropertiesForm form = new([File("/srv/a.txt")], RemoteFileSystemFeatures.Permissions, "abc", asRoot: false, canElevate: false);

		form.Owner.Text = "root";

		Assert.False(form.CanEditOwnership);
		Assert.True(form.CanEditPermissions);
		Assert.Null(form.BuildChange());
	}

	[Fact]
	public void RootLogin_NeverNeedsElevation()
	{
		PropertiesForm form = new([File("/srv/a.txt")], Editable, "root", asRoot: false, canElevate: false);

		form.Owner.Text = "www-data";

		Assert.True(form.RunsAsRoot);
		Assert.False(form.NeedsRootForOwner);
		Assert.False(Change(form).AsRoot);
	}

	[Fact]
	public void Root_CannotBeChosenWithoutElevation() =>
		Assert.False(new PropertiesForm([File("/srv/a.txt")], Editable, "abc", asRoot: true, canElevate: false).AsRoot);

	[Fact]
	public void Location_IsTheFolderHoldingTheWholeSelection()
	{
		Assert.Equal("/srv", Form(File("/srv/a.txt"), Folder("/srv/b")).Location);
		Assert.Equal("/var/www", Form(File("/var/www/a/x.txt"), File("/var/www/b/y.txt")).Location);
		Assert.Equal("/", Form(File("/etc/hosts"), File("/srv/a.txt")).Location);
	}

	[Fact]
	public void SelectedFileBytes_AddUpRegularFilesOnly() =>
		Assert.Equal(30, Form(File("/srv/a", size: 10), File("/srv/b", size: 20), Folder("/srv/c"), Link("/srv/d")).SelectedFileBytes);

	private static PropertiesForm Form(params RemoteFileEntry[] entries) =>
		new(entries, Editable, "abc", asRoot: false, canElevate: true);

	private static PropertiesChange Change(PropertiesForm form) => Assert.IsType<PropertiesChange>(form.BuildChange());
}
