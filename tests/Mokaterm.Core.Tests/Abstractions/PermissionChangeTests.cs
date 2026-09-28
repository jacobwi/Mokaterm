using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class PermissionChangeTests
{
	[Fact]
	public void WholeMode_ReplacesTheCurrentOne()
	{
		PermissionChange change = new() { Mode = Mode("750") };

		Assert.False(change.NeedsCurrentMode);
		Assert.Equal(Mode("750"), change.Apply(Mode("4777"), isDirectory: false));
	}

	[Fact]
	public void Mask_LeavesTheOtherBitsAsTheyAre()
	{
		PermissionChange change = new() { Mode = UnixFileMode.GroupWrite, Mask = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite };

		Assert.True(change.NeedsCurrentMode);
		Assert.Equal(Mode("2775"), change.Apply(Mode("2757"), isDirectory: true));
	}

	[Theory]
	[InlineData("644", false, "644")]
	[InlineData("744", false, "755")]
	[InlineData("600", true, "755")]
	[InlineData("711", false, "755")]
	public void ConditionalExecute_OnlyReachesFoldersAndFilesThatAreExecutable(string current, bool isDirectory, string expected)
	{
		PermissionChange change = new() { Mode = Mode("755"), ConditionalExecute = true };

		Assert.True(change.NeedsCurrentMode);
		Assert.Equal(Mode(expected), change.Apply(Mode(current), isDirectory));
	}

	[Fact]
	public void ConditionalExecute_StillClearsExecuteBits() =>
		Assert.Equal(Mode("640"), new PermissionChange { Mode = Mode("640"), ConditionalExecute = true }.Apply(Mode("755"), isDirectory: false));

	[Theory]
	[InlineData(PermissionTargets.All, RemoteEntryKind.SymbolicLink, true)]
	[InlineData(PermissionTargets.All, RemoteEntryKind.File, true)]
	[InlineData(PermissionTargets.Folders, RemoteEntryKind.Directory, true)]
	[InlineData(PermissionTargets.Folders, RemoteEntryKind.File, false)]
	[InlineData(PermissionTargets.Folders, RemoteEntryKind.SymbolicLink, false)]
	[InlineData(PermissionTargets.Files, RemoteEntryKind.File, true)]
	[InlineData(PermissionTargets.Files, RemoteEntryKind.Other, true)]
	[InlineData(PermissionTargets.Files, RemoteEntryKind.Directory, false)]
	[InlineData(PermissionTargets.Files, RemoteEntryKind.SymbolicLink, false)]
	public void Targets_PickTheKindsThatChange(PermissionTargets targets, RemoteEntryKind kind, bool expected) =>
		Assert.Equal(expected, new PermissionChange { Mode = Mode("644"), Targets = targets }.Includes(kind));

	private static UnixFileMode Mode(string octal) => (UnixFileMode)Convert.ToInt32(octal, 8);
}
