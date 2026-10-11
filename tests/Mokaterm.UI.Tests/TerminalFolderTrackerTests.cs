using Mokaterm.UI.Terminal;
using Mokaterm.UI.Terminal.Internal;

namespace Mokaterm.UI.Tests;

public sealed class TerminalFolderTrackerTests
{
	[Theory]
	[InlineData("file://box/home/abc/src", "box", "/home/abc/src")]
	[InlineData("file:///var/log", null, "/var/log")]
	[InlineData("file://localhost/etc", null, "/etc")]
	[InlineData("file://box.example.com/srv/my%20site/%C3%A9t%C3%A9", "box.example.com", "/srv/my site/été")]
	[InlineData("FILE://box/a//b/../c/", "box", "/a/c")]
	[InlineData("kitty-shell-cwd://box/srv/50%", "box", "/srv/50%")]
	public void Osc7_GivesTheMachineAndTheAbsolutePath(string data, string? machine, string path)
	{
		Assert.True(TerminalFolderTracker.TryParseOsc7(data, out string? parsedMachine, out string parsedPath));
		Assert.Equal(machine, parsedMachine);
		Assert.Equal(path, parsedPath);
	}

	[Theory]
	[InlineData("")]
	[InlineData("/home/abc")]
	[InlineData("http://box/home")]
	[InlineData("file://box")]
	[InlineData("file://box/tmp/%0Aevil")]
	public void Osc7_ThatIsNotAFolder_IsRefused(string data) =>
		Assert.False(TerminalFolderTracker.TryParseOsc7(data, out _, out _));

	[Fact]
	public void Osc7_PastTheLengthCap_IsRefused() =>
		Assert.False(TerminalFolderTracker.TryParseOsc7("file://box/" + new string('a', TerminalFolderTracker.MaxReportLength), out _, out _));

	[Fact]
	public void Prompt_IsPassedOnOncePerChange()
	{
		TerminalFolderTracker tracker = new();

		Assert.Equal(new TerminalFolder("~/src", "abc"), tracker.FromPrompt("~/src", "abc", "box"));
		Assert.Null(tracker.FromPrompt("~/src", "abc", "box"));
		Assert.Equal(new TerminalFolder("/etc", "abc"), tracker.FromPrompt("/etc", "abc", "box"));
		Assert.Equal(new TerminalFolder("~/src", "abc"), tracker.FromPrompt("~/src", "abc", "box"));
	}

	// "ssh other" inside the session prints the other machine's prompts, whose folders are not on this server.
	[Fact]
	public void Prompt_FromAnotherMachine_IsDropped()
	{
		TerminalFolderTracker tracker = new();
		Assert.NotNull(tracker.FromPrompt("~", "abc", "box"));

		Assert.Null(tracker.FromPrompt("/var/www", "abc", "web01"));
		Assert.Equal(new TerminalFolder("/opt", "abc"), tracker.FromPrompt("/opt", "abc", "BOX"));
	}

	[Fact]
	public void Osc7_AndPromptAgreeOnTheShortMachineName()
	{
		TerminalFolderTracker tracker = new();
		Assert.NotNull(tracker.FromPrompt("~", "abc", "box"));

		Assert.Equal(new TerminalFolder("/srv", null), tracker.FromOsc7("file://box.example.com/srv"));
		Assert.Null(tracker.FromOsc7("file://web01.example.com/srv/other"));
	}

	// Once the shell says where it is itself, a prompt has nothing to add.
	[Fact]
	public void Prompts_AfterAnOsc7_AreIgnored()
	{
		TerminalFolderTracker tracker = new();
		Assert.NotNull(tracker.FromOsc7("file://box/srv"));

		Assert.Null(tracker.FromPrompt("~/elsewhere", "abc", "box"));
		Assert.Equal(new TerminalFolder("/tmp", null), tracker.FromOsc7("file://box/tmp"));
	}

	[Fact]
	public void Reset_ForgetsTheMachineAndTheLastFolder()
	{
		TerminalFolderTracker tracker = new();
		Assert.NotNull(tracker.FromOsc7("file://box/srv"));

		tracker.Reset();

		Assert.Equal(new TerminalFolder("/srv", "abc"), tracker.FromPrompt("/srv", "abc", "other"));
	}

	[Theory]
	[InlineData("")]
	[InlineData("/tmp/\u001b[31m")]
	public void Prompt_ThatIsNotAFolder_IsDropped(string folder) =>
		Assert.Null(new TerminalFolderTracker().FromPrompt(folder, "abc", "box"));
}
