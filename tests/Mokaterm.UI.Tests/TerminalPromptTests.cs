using Mokaterm.UI.Terminal.Internal;

namespace Mokaterm.UI.Tests;

public sealed class TerminalPromptTests
{
	[Theory]
	// Bash and zsh on Linux and macOS.
	[InlineData("bc@build-01:~$ ls -la", "ls -la")]
	[InlineData("bc@build-01:~/src/mokaterm$ dotnet build", "dotnet build")]
	[InlineData("root@db-01:/var/log# tail -f syslog", "tail -f syslog")]
	[InlineData("[bc@build-01 mokaterm]$ git status", "git status")]
	[InlineData("(venv) bc@build-01:~$ python -m pytest", "python -m pytest")]
	[InlineData("bc@mac ~ % brew upgrade", "brew upgrade")]
	// Windows shells.
	[InlineData(@"PS C:\Users\bc> git pull", "git pull")]
	[InlineData(@"PS C:\Users\bc\Source\Mokaterm> dotnet test", "dotnet test")]
	[InlineData(@"C:\Users\bc>dir /b", "dir /b")]
	// A prompt cut down to its last character, and a continuation line.
	[InlineData("$ whoami", "whoami")]
	[InlineData("# systemctl restart nginx", "systemctl restart nginx")]
	[InlineData("> done", "done")]
	// Trailing spaces and redirections inside the command survive.
	[InlineData("bc@build-01:~$ echo $HOME > /tmp/home   ", "echo $HOME > /tmp/home")]
	[InlineData("bc@build-01:~$ grep -r 'a b' . | wc -l", "grep -r 'a b' . | wc -l")]
	public void TryExtractCommand_RealPrompts_ReturnTheCommand(string row, string expected)
	{
		Assert.True(TerminalPrompt.TryExtractCommand(row, out string command));
		Assert.Equal(expected, command);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData(null)]
	// Output, not a command.
	[InlineData("total 48")]
	[InlineData("-rw-r--r--  1 bc bc  1.2K Sep 17 08:00 build.log")]
	[InlineData("Listening on http://localhost:5080")]
	[InlineData("2026-09-17 08:00:01 INFO  starting worker")]
	// A prompt with nothing typed after it yet.
	[InlineData("bc@build-01:~$ ")]
	[InlineData("$")]
	[InlineData(@"PS C:\Users\bc>")]
	public void TryExtractCommand_RowsThatAreNotCommands_ReturnFalse(string? row)
	{
		Assert.False(TerminalPrompt.TryExtractCommand(row, out string command));
		Assert.Equal("", command);
	}

	[Fact]
	public void TryExtractCommand_VeryLongRow_IsTreatedAsOutput() =>
		Assert.False(TerminalPrompt.TryExtractCommand("bc@build-01:~$ " + new string('x', 4000), out _));
}
