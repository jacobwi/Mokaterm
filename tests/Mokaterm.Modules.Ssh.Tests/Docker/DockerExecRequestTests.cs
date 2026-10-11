using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>Running one command in a container without a terminal: the words docker gets, and what is refused first.</summary>
public sealed class DockerExecRequestTests
{
	[Fact]
	public void Sh_RunsTheCommandAsOneScript()
	{
		DockerExecRequest request = new() { Container = "shop-api-1", Command = "  ps aux | head -n 5\n" };

		Assert.Equal(["exec", "shop-api-1", "sh", "-c", "ps aux | head -n 5"], request.Arguments());
		Assert.Equal("docker exec shop-api-1 sh -c 'ps aux | head -n 5'", request.ToCall().Display);
	}

	[Fact]
	public void Bash_AndEveryOption()
	{
		DockerExecRequest request = new()
		{
			Container = "shop-api-1",
			Command = "echo \"$HOME\" it's",
			Shell = DockerExecShell.Bash,
			User = "www-data:www-data",
			WorkingDirectory = "/var/www/my site",
			Environment = ["A=1", "B=two words", "C="],
		};

		Assert.Equal(
			["exec", "-u", "www-data:www-data", "-w", "/var/www/my site", "-e", "A=1", "-e", "B=two words", "-e", "C=", "shop-api-1", "bash", "-c", "echo \"$HOME\" it's"],
			request.Arguments());
		Assert.Equal(
			"docker exec -u www-data:www-data -w '/var/www/my site' -e A=1 -e 'B=two words' -e C= shop-api-1 bash -c 'echo \"$HOME\" it'\\''s'",
			request.ToCall().Display);
	}

	[Fact]
	public void NoShell_SplitsTheWordsTheWayAShellWould_AndRunsThemAsTheyAre()
	{
		DockerExecRequest request = new() { Container = "web", Command = "printf '%s\\n' \"a b\" -n --flag c\\ d", Shell = DockerExecShell.None };

		Assert.Equal(["exec", "web", "printf", "%s\\n", "a b", "-n", "--flag", "c d"], request.Arguments());
	}

	[Fact]
	public void TheCall_HasTheBoxsOwnLimits()
	{
		DockerCall call = new DockerExecRequest { Container = "web", Command = "true" }.ToCall();

		Assert.Equal(DockerExecRequest.Timeout, call.Timeout);
		Assert.Equal(DockerExecRequest.OutputLimitBytes, call.OutputLimitBytes);
	}

	[Theory]
	[InlineData("-it", "id", DockerExecShell.Sh, "", "", "cannot start with '-'")]
	[InlineData("web", "   ", DockerExecShell.Sh, "", "", "Type a command to run.")]
	[InlineData("web", "echo 'unclosed", DockerExecShell.None, "", "", "quote")]
	[InlineData("web", "line\nbreak", DockerExecShell.None, "", "", "Without a shell the command has to be one line")]
	[InlineData("web", "echo \u0007", DockerExecShell.Sh, "", "", "control character")]
	[InlineData("web", "id", DockerExecShell.Sh, "-root", "", "The user is a name or a number")]
	[InlineData("web", "id", DockerExecShell.Sh, "root user", "", "The user is a name or a number")]
	[InlineData("web", "id", DockerExecShell.Sh, "", "var/www", "not an absolute path")]
	public void Problem_IsSaidBeforeAnythingRuns(string container, string command, DockerExecShell shell, string user, string folder, string expected)
	{
		DockerExecRequest request = new() { Container = container, Command = command, Shell = shell, User = user, WorkingDirectory = folder };

		Assert.Contains(expected, request.Problem(), StringComparison.Ordinal);
		Assert.Throws<ArgumentException>(() => request.Arguments());
	}

	[Theory]
	[InlineData("1A=x")]
	[InlineData("=x")]
	[InlineData("NOEQUALS")]
	[InlineData("A B=x")]
	public void Problem_AVariableThatIsNotNameEqualsValue(string variable)
	{
		DockerExecRequest request = new() { Container = "web", Command = "env", Environment = [variable] };

		Assert.StartsWith("Each variable is NAME=value", request.Problem(), StringComparison.Ordinal);
	}

	[Fact]
	public void AShellScript_MayHoldTabsAndLineBreaks()
	{
		DockerExecRequest request = new() { Container = "web", Command = "for i in 1 2; do\r\n\techo $i\r\ndone" };

		Assert.Null(request.Problem());
		Assert.Equal("for i in 1 2; do\n\techo $i\ndone", request.Arguments()[^1]);
	}

	[Fact]
	public void ParseEnvironment_OnePerLine_BlankLinesDropped()
	{
		Assert.Equal(["A=1", "B=2"], DockerExecRequest.ParseEnvironment(" A=1 \r\n\r\nB=2\n"));
		Assert.Empty(DockerExecRequest.ParseEnvironment(null));
	}

	[Fact]
	public void ToString_NeverPrintsTheCommand()
	{
		DockerExecRequest request = new() { Container = "web", Command = "curl -H 'Authorization: Bearer abc123' http://x", Environment = ["TOKEN=abc123"] };

		Assert.DoesNotContain("abc123", request.ToString(), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("a b  c", new[] { "a", "b", "c" })]
	[InlineData("'single $x' \"double $x\"", new[] { "single $x", "double $x" })]
	[InlineData("\"esc \\\" quote\" back\\\\slash", new[] { "esc \" quote", "back\\slash" })]
	[InlineData("''", new[] { "" })]
	[InlineData("a''b", new[] { "ab" })]
	public void Words_SplitAsAShellWould(string text, string[] expected)
	{
		(IReadOnlyList<string> words, string? problem) = DockerExecWords.Split(text);

		Assert.Null(problem);
		Assert.Equal(expected, words);
	}

	[Theory]
	[InlineData("'open")]
	[InlineData("\"open")]
	[InlineData("a \"b 'c\" 'd")]
	public void Words_AQuoteThatNeverCloses_IsAProblem(string text) => Assert.NotNull(DockerExecWords.Split(text).Problem);
}
