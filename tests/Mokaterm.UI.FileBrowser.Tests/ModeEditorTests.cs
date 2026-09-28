using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Properties;
using static Mokaterm.UI.FileBrowser.Tests.Fakes.FakeFileSystem;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class ModeEditorTests
{
	[Fact]
	public void OneMode_IsFullyKnown()
	{
		ModeEditor editor = new([Mode("644")]);

		Assert.False(editor.WasMixed);
		Assert.Equal("644", editor.OctalText);
		Assert.Equal("rw-r--r--", editor.Symbolic);
		Assert.True(editor.Get(UnixFileMode.UserWrite));
		Assert.False(editor.Get(UnixFileMode.GroupWrite));
		Assert.False(editor.IsChanged);
	}

	[Fact]
	public void DifferentModes_KnowOnlyTheBitsTheyShare()
	{
		ModeEditor editor = new([Mode("644"), Mode("755")]);

		Assert.True(editor.WasMixed);
		Assert.Equal("", editor.OctalText);
		Assert.True(editor.IsOctalValid);
		Assert.Null(editor.Get(UnixFileMode.UserExecute));
		Assert.True(editor.Get(UnixFileMode.UserRead));
		Assert.False(editor.Get(UnixFileMode.GroupWrite));
		Assert.Equal("rw?r-?r-?", editor.Symbolic);
	}

	[Fact]
	public void UnreportedMode_LeavesEveryBitMixed()
	{
		ModeEditor editor = new([Mode("644"), null]);

		Assert.Equal(UnixFileMode.None, editor.Known);
		Assert.Equal("?????????", editor.Symbolic);
	}

	[Fact]
	public void SettingABitBack_IsNoChange()
	{
		ModeEditor editor = new([Mode("644")]);

		editor.Set(UnixFileMode.GroupWrite, true);
		Assert.True(editor.IsChanged);
		Assert.Equal("664", editor.OctalText);

		editor.Set(UnixFileMode.GroupWrite, false);
		Assert.False(editor.IsChanged);
		Assert.Equal("644", editor.OctalText);
	}

	[Fact]
	public void SettingAMixedBit_ChangesOnlyThatBitOnEachEntry()
	{
		ModeEditor editor = new([Mode("644"), Mode("755")]);

		editor.Set(UnixFileMode.UserExecute, false);
		PermissionChange change = editor.ToChange(recursive: false, PermissionTargets.All, conditionalExecute: false);

		Assert.True(editor.IsChanged);
		Assert.True(editor.IsMixed);
		Assert.Equal(Mode("655"), change.Apply(Mode("755"), isDirectory: false));
		Assert.Equal(Mode("644"), change.Apply(Mode("644"), isDirectory: false));
	}

	[Fact]
	public void TypingAMode_SetsEveryBit()
	{
		ModeEditor editor = new([Mode("644"), Mode("755")]);

		editor.SetOctal("2750");

		Assert.False(editor.IsMixed);
		Assert.True(editor.IsChanged);
		Assert.Equal("rwxr-s---", editor.Symbolic);
		Assert.Equal(PermissionChange.AllBits, editor.ToChange(recursive: false, PermissionTargets.All, conditionalExecute: false).Mask);
	}

	[Theory]
	[InlineData("")]
	[InlineData("8")]
	[InlineData("75")]
	[InlineData("rwx")]
	public void InvalidOctal_StaysInvalidUntilABitIsSet(string text)
	{
		ModeEditor editor = new([Mode("644")]);

		editor.SetOctal(text);
		Assert.False(editor.IsOctalValid);

		editor.Set(UnixFileMode.OtherRead, true);
		Assert.True(editor.IsOctalValid);
		Assert.Equal("644", editor.OctalText);
	}

	[Fact]
	public void MixedModes_MayLeaveTheOctalFieldEmpty()
	{
		ModeEditor editor = new([Mode("644"), Mode("755")]);

		editor.SetOctal("7");
		Assert.False(editor.IsOctalValid);

		editor.SetOctal(" ");
		Assert.True(editor.IsOctalValid);
		Assert.True(editor.IsMixed);
		Assert.False(editor.IsChanged);
	}
}
