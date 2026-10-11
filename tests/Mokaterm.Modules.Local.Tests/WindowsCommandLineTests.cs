using Mokaterm.Modules.Local.Pty.Windows;

namespace Mokaterm.Modules.Local.Tests;

/// <summary>
/// The quoting CommandLineToArgvW and the C runtime split by. The expected strings are written the way they appear on a
/// command line, so each case reads as what a program would be handed.
/// </summary>
public sealed class WindowsCommandLineTests
{
	[Theory]
	[InlineData("plain", "plain")]
	[InlineData("", "\"\"")]
	[InlineData("two words", "\"two words\"")]
	[InlineData("tab\there", "\"tab\there\"")]
	[InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
	[InlineData("C:\\path\\", "C:\\path\\")]
	[InlineData("C:\\with space\\", "\"C:\\with space\\\\\"")]
	[InlineData("a\\\\\"b", "\"a\\\\\\\\\\\"b\"")]
	[InlineData("back\\slash", "back\\slash")]
	public void Quote_FollowsTheRuntimeRules(string argument, string expected)
	{
		Assert.Equal(expected, WindowsCommandLine.Quote(argument));
	}

	[Fact]
	public void Build_QuotesAProgramPathWithASpace_AndNothingElseAboutIt()
	{
		string line = WindowsCommandLine.Build(@"C:\Program Files\Git\bin\bash.exe", ["--login", "-i"], null);

		Assert.Equal("\"C:\\Program Files\\Git\\bin\\bash.exe\" --login -i", line);
	}

	[Fact]
	public void Build_QuotesTheModulesOwnArguments()
	{
		string line = WindowsCommandLine.Build(@"C:\Windows\System32\wsl.exe", ["-d", "Ubuntu", "--cd", @"C:\My Projects"], null);

		Assert.Equal("C:\\Windows\\System32\\wsl.exe -d Ubuntu --cd \"C:\\My Projects\"", line);
	}

	[Fact]
	public void Build_AddsTypedTextAsItIs()
	{
		// A command line typed for cmd.exe follows cmd.exe's own rules, which only cmd.exe knows.
		string line = WindowsCommandLine.Build(@"C:\Windows\System32\cmd.exe", [], "  /k \"echo hi & title x\"  ");

		Assert.Equal("C:\\Windows\\System32\\cmd.exe /k \"echo hi & title x\"", line);
	}

	[Fact]
	public void Build_WithoutArguments_IsTheProgramAlone()
	{
		Assert.Equal(@"C:\Windows\System32\cmd.exe", WindowsCommandLine.Build(@"C:\Windows\System32\cmd.exe", [], "   "));
	}
}
