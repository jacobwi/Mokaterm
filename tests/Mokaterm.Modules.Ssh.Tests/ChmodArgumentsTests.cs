using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class ChmodArgumentsTests
{
	[Theory]
	[InlineData("755", "00755")]
	[InlineData("2775", "02775")]
	[InlineData("0", "00000")]
	public void WholeMode_IsFiveOctalDigits(string mode, string expected) =>
		Assert.Equal(expected, ChmodArguments.Mode(new PermissionChange { Mode = Mode(mode) }));

	[Fact]
	public void PartialMask_SetsAndClearsOnlyItsBits()
	{
		PermissionChange change = new()
		{
			Mode = UnixFileMode.GroupWrite,
			Mask = UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute,
		};

		Assert.Equal("g+w,o-rwx", ChmodArguments.Mode(change));
	}

	[Fact]
	public void ConditionalExecute_AddsWithCapitalX_AndRemovalsComeLast() =>
		Assert.Equal(
			"u+rwX,g+rX,o+rX,g-w,o-w,u-s,g-s,a-t",
			ChmodArguments.Mode(new PermissionChange { Mode = Mode("755"), ConditionalExecute = true }));

	[Fact]
	public void SpecialBits_UseTheClassesBusyBoxAccepts()
	{
		PermissionChange change = new()
		{
			Mode = UnixFileMode.SetGroup | UnixFileMode.StickyBit,
			Mask = UnixFileMode.SetUser | UnixFileMode.SetGroup | UnixFileMode.StickyBit,
		};

		Assert.Equal("g+s,a+t,u-s", ChmodArguments.Mode(change));
	}

	[Fact]
	public void EmptyMask_HasNothingToRun() =>
		Assert.Null(ChmodArguments.Mode(new PermissionChange { Mode = Mode("777"), Mask = UnixFileMode.None }));

	[Theory]
	[InlineData(PermissionTargets.All, "")]
	[InlineData(PermissionTargets.Folders, "d")]
	[InlineData(PermissionTargets.Files, "f")]
	public void Targets_MapToTheScriptFlag(PermissionTargets targets, string expected) =>
		Assert.Equal(expected, ChmodArguments.Targets(targets));

	private static UnixFileMode Mode(string octal) => (UnixFileMode)Convert.ToInt32(octal, 8);
}
