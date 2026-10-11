using Mokaterm.UI.FileBrowser.Browsing;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class TerminalFolderTargetTests
{
	[Theory]
	[InlineData("/etc/nginx", null, "/etc/nginx")]
	[InlineData("/var//log/../www/", "abc", "/var/www")]
	[InlineData("~", null, "/home/abc")]
	[InlineData("~", "abc", "/home/abc")]
	[InlineData("~/src/app", "abc", "/home/abc/src/app")]
	[InlineData("~/", "abc", "/home/abc")]
	public void Resolve_PlacesAbsolutePathsAndTheLoginsHome(string folder, string? user, string expected) =>
		Assert.Equal(expected, TerminalFolderTarget.Resolve(folder, user, "abc", "/home/abc"));

	// After "sudo -i" the prompt says root@box:~#, and ~ is root's home rather than the login's.
	[Fact]
	public void Resolve_TildeOfRoot_IsRootsHome() =>
		Assert.Equal("/root/.ssh", TerminalFolderTarget.Resolve("~/.ssh", "root", "abc", "/home/abc"));

	[Fact]
	public void Resolve_LoginAsRoot_UsesItsOwnHome() =>
		Assert.Equal("/var/lib/root", TerminalFolderTarget.Resolve("~", "root", "root", "/var/lib/root"));

	[Theory]
	[InlineData("~", "alice")]
	[InlineData("~alice/src", "abc")]
	[InlineData("nginx", "abc")]
	[InlineData("", "abc")]
	[InlineData("/tmp/a\u0007b", "abc")]
	public void Resolve_WhatCannotBePlaced_IsNull(string folder, string user) =>
		Assert.Null(TerminalFolderTarget.Resolve(folder, user, "abc", "/home/abc"));
}
