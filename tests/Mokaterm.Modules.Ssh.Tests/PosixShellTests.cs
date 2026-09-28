using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class PosixShellTests
{
	[Theory]
	[InlineData("", "''")]
	[InlineData("plain", "'plain'")]
	[InlineData("/var/www/my site", "'/var/www/my site'")]
	[InlineData("it's", @"'it'\''s'")]
	[InlineData("''", @"''\'''\'''")]
	[InlineData("$(rm -rf /) `id` $HOME \\ \" *", "'$(rm -rf /) `id` $HOME \\ \" *'")]
	[InlineData("line one\nline two\ttab", "'line one\nline two\ttab'")]
	[InlineData("données/日本語/файл", "'données/日本語/файл'")]
	[InlineData("-rf", "'-rf'")]
	public void Quote_ProducesOneSingleQuotedWord(string value, string expected) =>
		Assert.Equal(expected, PosixShell.Quote(value));

	[Fact]
	public void Quote_RejectsNul() =>
		Assert.Throws<ArgumentException>(() => PosixShell.Quote("a\0b"));

	[Fact]
	public void ScriptCommand_PassesArgumentsPositionally()
	{
		string command = PosixShell.ScriptCommand("cat -- \"$1\"", ["/tmp/it's here", "x"]);

		Assert.Equal(@"sh -c 'cat -- ""$1""' sh '/tmp/it'\''s here' 'x'", command);
	}

	[Fact]
	public async Task Quote_RoundTripsThroughAPosixShell()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		string value = "it's $HOME `x` \\ \"q\" *\nsecond line";
		(int exitCode, byte[] output, string error) = await PosixShellLocator.RunAsync(shell, "printf '%s' " + PosixShell.Quote(value), []);

		Assert.True(exitCode == 0, error);
		Assert.Equal(value, System.Text.Encoding.UTF8.GetString(output));
	}
}
