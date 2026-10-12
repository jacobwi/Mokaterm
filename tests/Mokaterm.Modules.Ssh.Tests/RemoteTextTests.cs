using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class RemoteTextTests
{
	private const char Esc = (char)0x1B;

	[Theory]
	[InlineData(null, "")]
	[InlineData("", "")]
	[InlineData("\n  \r\n", "")]
	[InlineData("\n  first \nsecond", "first")]
	[InlineData("only\r", "only")]
	public void FirstLine_IsTheFirstLineWithText_Trimmed(string? text, string expected) =>
		Assert.Equal(expected, RemoteText.FirstLine(text));

	[Fact]
	public void FirstLine_DropsColours_AndALineThatWasOnlyEscapes()
	{
		Assert.Equal("Error: no such container", RemoteText.FirstLine($"{Esc}[0m\n{Esc}[31mError:{Esc}[0m no such container\n"));
	}

	[Fact]
	public void FirstLine_ALongLine_IsCut()
	{
		string line = RemoteText.FirstLine(new string('x', 1000));

		Assert.Equal(303, line.Length);
		Assert.EndsWith("...", line, StringComparison.Ordinal);
	}

	[Fact]
	public void Problem_QuotesStderr_OrNamesTheExit()
	{
		Assert.Equal("unit nope.service not found", new AdminOutcome { ExitCode = 4, Error = $"\n{Esc}[1munit nope.service not found{Esc}[0m\n" }.Problem());
		Assert.Equal("The command ended with exit code 3.", new AdminOutcome { ExitCode = 3 }.Problem());
		Assert.Equal("The command was ended by KILL.", new AdminOutcome { ExitSignal = "KILL" }.Problem());
	}
}
