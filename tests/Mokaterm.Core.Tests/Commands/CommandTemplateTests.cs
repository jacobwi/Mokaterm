using Mokaterm.Abstractions.Commands;

namespace Mokaterm.Core.Tests.Commands;

/// <summary>
/// How a saved command reads for placeholders: what is ours, what another tool's braces keep, the escape, what is
/// broken, and the values going in exactly as typed.
/// </summary>
public sealed class CommandTemplateTests
{
	[Fact]
	public void Parse_NamesAndDefaults_AreListedOnceInTheOrderTheyAppear()
	{
		CommandTemplate template = CommandTemplate.Parse("ssh -p {{port:22}} {{user}}@{{host:localhost}} uptime");

		Assert.Null(template.Problem);
		Assert.True(template.HasPlaceholders);
		Assert.Equal(
			[new CommandPlaceholder("port", "22"), new CommandPlaceholder("user", null), new CommandPlaceholder("host", "localhost")],
			template.Placeholders);
	}

	[Fact]
	public void Parse_SplitsTheCommandIntoTextAndPlaceholders()
	{
		CommandTemplate template = CommandTemplate.Parse("tail -n {{lines:50}} /var/log/{{file}}");

		Assert.Equal(["tail -n ", "{{lines:50}}", " /var/log/", "{{file}}"], template.Parts.Select(part => part.Text));
		Assert.Equal([false, true, false, true], template.Parts.Select(part => part.IsPlaceholder));
		Assert.Equal("50", template.Parts[1].Default);
		Assert.Equal("file", template.Parts[3].Name);
	}

	[Fact]
	public void TheSameNameTwice_IsAskedForOnce_AndFilledEverywhere()
	{
		CommandTemplate template = CommandTemplate.Parse("cp {{name}}.conf {{name}}.conf.bak");

		CommandPlaceholder placeholder = Assert.Single(template.Placeholders);
		Assert.Equal("name", placeholder.Name);
		Assert.Equal("cp nginx.conf nginx.conf.bak", template.Fill(Values(("name", "nginx"))));
	}

	[Fact]
	public void ADefaultGivenAtOneUseOnly_CountsForEveryUse()
	{
		CommandTemplate template = CommandTemplate.Parse("nc -z {{host}} {{port}} && echo {{port:443}} open");

		Assert.Null(template.Problem);
		Assert.Equal("443", template.DefaultOf("port"));
		Assert.Equal("nc -z db 443 && echo 443 open", template.Fill(Values(("host", "db"))));
	}

	[Fact]
	public void TwoDifferentDefaults_AreAProblem()
	{
		CommandTemplate template = CommandTemplate.Parse("echo {{port:22}} {{port:2222}}");

		Assert.Equal("{{port}} has two different defaults. Give it one.", template.Problem);
	}

	[Fact]
	public void NamesTakeLettersDigitsUnderscoresDashesAndDots()
	{
		CommandTemplate template = CommandTemplate.Parse("echo {{my-var.v2_x}} {{1}} {{_tmp}}");

		Assert.Equal(["my-var.v2_x", "1", "_tmp"], template.Placeholders.Select(placeholder => placeholder.Name));
	}

	// The commands this rule exists for: Go templates in docker, kubectl and gh, Jinja written with spaces, Mustache's
	// triple braces, bash brace expansion and a lone pair of braces all type exactly as written and ask for nothing.
	[Theory]
	[InlineData("docker ps --format '{{.Names}}'")]
	[InlineData("docker ps -a --format 'table {{.ID}} {{.Names}} {{.Status}}'")]
	[InlineData("docker inspect -f '{{json .State}}' web")]
	[InlineData("docker inspect -f '{{.State.Health.Status}}' web")]
	[InlineData("docker inspect -f '{{range .Mounts}}{{.Source}} {{end}}' web")]
	[InlineData("docker inspect -f '{{if .State.Running}}up{{else}}down{{end}}' web")]
	[InlineData("docker inspect -f '{{index .Config.Labels \"com.docker.compose.project\"}}' web")]
	[InlineData("kubectl get pods -o go-template='{{range .items}}{{.metadata.name}}{{\"\\n\"}}{{end}}'")]
	[InlineData("gh pr list --json number --template '{{range .}}{{.number}} {{end}}'")]
	[InlineData("docker ps --format '{{- .Names -}}'")]
	[InlineData("docker ps --format '{{$name := .Names}}{{$name}}'")]
	[InlineData("ansible all -m debug -a 'msg={{ inventory_hostname }}'")]
	[InlineData("echo '{{{triple}}}'")]
	[InlineData("echo {{a,b},c}")]
	[InlineData("echo {{")]
	[InlineData("echo }} {")]
	public void AnotherToolsBraces_StayAsTyped(string command)
	{
		CommandTemplate template = CommandTemplate.Parse(command);

		Assert.False(template.HasPlaceholders);
		Assert.Null(template.Problem);
		Assert.Equal(command, template.Fill(null));
		Assert.Equal(command, CommandTemplate.EscapeText(command));
	}

	[Fact]
	public void AGoTemplateAndAPlaceholder_LiveInOneCommand()
	{
		CommandTemplate template = CommandTemplate.Parse("docker ps --filter name={{name}} --format '{{.Names}} {{.Status}}'");

		Assert.Equal("name", Assert.Single(template.Placeholders).Name);
		Assert.Equal("docker ps --filter name=web --format '{{.Names}} {{.Status}}'", template.Fill(Values(("name", "web"))));
	}

	[Theory]
	[InlineData("{{end}}")]
	[InlineData("{{else}}")]
	[InlineData("{{break}}")]
	[InlineData("{{continue}}")]
	public void TheWordsGoTemplatesWriteAlone_AreNeverAskedFor(string word) =>
		Assert.False(CommandTemplate.Parse($"echo {word}").HasPlaceholders);

	[Fact]
	public void ATemplateWordWithADefault_IsAPlaceholder() =>
		Assert.Equal("end", Assert.Single(CommandTemplate.Parse("date -d {{end:tomorrow}}").Placeholders).Name);

	[Theory]
	[InlineData("echo {{{{name}}", "echo {{name}}")]
	[InlineData("echo {{{{.Names}}", "echo {{.Names}}")]
	[InlineData("ansible all -a 'msg={{{{inventory_hostname}}'", "ansible all -a 'msg={{inventory_hostname}}'")]
	[InlineData("printf '{{{{{{{{'", "printf '{{{{'")]
	public void FourBraces_TypeTwo_AndAreNeverAPlaceholder(string command, string typed)
	{
		CommandTemplate template = CommandTemplate.Parse(command);

		Assert.False(template.HasPlaceholders);
		Assert.Null(template.Problem);
		Assert.Equal(typed, template.Fill(null));
	}

	[Fact]
	public void AnEscapeNextToAPlaceholder_KeepsBoth() =>
		Assert.Equal("echo {{x}} = 5", CommandTemplate.Parse("echo {{{{x}} = {{x}}").Fill(Values(("x", "5"))));

	[Theory]
	[InlineData("echo {{}}", "\"{{}}\" has no name.")]
	[InlineData("echo {{:fallback}}", "\"{{:fallback}}\" has no name.")]
	[InlineData("ping {{host", "\"{{host\" is not closed.")]
	[InlineData("ping {{host} -c 1", "\"{{host\" is not closed.")]
	[InlineData("ping {{host:localhost -c 1", "\"{{host:localhost -c 1\" is not closed.")]
	public void ABrokenPlaceholder_IsAProblemThatSaysWhere(string command, string start)
	{
		CommandTemplate template = CommandTemplate.Parse(command);

		Assert.NotNull(template.Problem);
		Assert.StartsWith(start, template.Problem, StringComparison.Ordinal);
		Assert.Contains("{{{{ for a literal {{", template.Problem, StringComparison.Ordinal);
	}

	[Fact]
	public void ABrokenPlaceholder_IsTypedAsItStands_AndTheRestStillAsks()
	{
		CommandTemplate template = CommandTemplate.Parse("echo {{}} {{host {{x}}");

		Assert.NotNull(template.Problem);
		Assert.Equal("x", Assert.Single(template.Placeholders).Name);
		Assert.Equal("echo {{}} {{host 1", template.Fill(Values(("x", "1"))));
	}

	[Fact]
	public void ADefaultDoesNotRunPastTheEndOfItsLine()
	{
		CommandTemplate template = CommandTemplate.Parse("echo {{a:one\necho two}}");

		Assert.False(template.HasPlaceholders);
		Assert.NotNull(template.Problem);
	}

	[Fact]
	public void ADefaultWithAControlCharacter_IsAProblem() =>
		Assert.Equal(
			"The default of {{sep}} holds a tab or another control character.",
			CommandTemplate.Parse("cut -d '{{sep:\t}}' -f1").Problem);

	[Fact]
	public void MoreValuesThanAPromptHolds_AreAProblem()
	{
		string command = string.Join(' ', Enumerable.Range(1, CommandTemplate.MaxPlaceholders + 1).Select(index => $"{{{{v{index}}}}}"));

		Assert.Equal("A command can ask for at most 20 values.", CommandTemplate.Parse(command).Problem);
		Assert.Null(CommandTemplate.Parse(string.Join(' ', Enumerable.Range(1, CommandTemplate.MaxPlaceholders).Select(index => $"{{{{v{index}}}}}"))).Problem);
	}

	[Fact]
	public void Fill_PutsValuesInExactlyAsTyped_WithNoQuoting()
	{
		CommandTemplate template = CommandTemplate.Parse("grep {{pattern}} '{{file}}'");

		Assert.Equal(
			"grep a b; $(whoami) 'my file.txt'",
			template.Fill(Values(("pattern", "a b; $(whoami)"), ("file", "my file.txt"))));
	}

	[Fact]
	public void Fill_WithoutAValue_TakesTheDefault_OrNothing() =>
		Assert.Equal("ls -l ", CommandTemplate.Parse("ls {{flags:-l}} {{path}}").Fill(null));

	[Theory]
	[InlineData("two\nlines")]
	[InlineData("carriage\rreturn")]
	[InlineData("a\ttab")]
	[InlineData("nul\0")]
	public void Fill_RefusesAValueWithAControlCharacter(string value)
	{
		CommandTemplate template = CommandTemplate.Parse("echo {{x}}");

		Assert.NotNull(CommandTemplate.ValueProblem(value));
		ArgumentException refused = Assert.Throws<ArgumentException>(() => template.Fill(Values(("x", value))));
		Assert.StartsWith("A value cannot hold a line break", refused.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ValueProblem_RefusesEscapeDeleteAndTheC1Range_AndTakesAnyOtherText()
	{
		Assert.NotNull(CommandTemplate.ValueProblem("a" + (char)27 + "[2J"));
		Assert.NotNull(CommandTemplate.ValueProblem("a" + (char)127));
		Assert.NotNull(CommandTemplate.ValueProblem("a" + (char)0x9B));
		Assert.Null(CommandTemplate.ValueProblem("it's \"quoted\" $HOME `id` | grep ; & ok"));
		Assert.Null(CommandTemplate.ValueProblem(""));
		Assert.Null(CommandTemplate.ValueProblem(null));
	}

	[Theory]
	[InlineData("echo {{name}}")]
	[InlineData("echo {{name:default value}} and {{other}}")]
	[InlineData("echo {{}} {{host {{x}")]
	[InlineData("echo {{{{name}}")]
	[InlineData("echo {{{name}}} {{{{{{x}}")]
	[InlineData("ansible all -a 'msg={{inventory_hostname}}' --format '{{.Names}}'")]
	[InlineData("{{a:{{b}}}}")]
	public void EscapeText_TypesBackExactlyWhatWasGiven(string text)
	{
		CommandTemplate escaped = CommandTemplate.Parse(CommandTemplate.EscapeText(text));

		Assert.False(escaped.HasPlaceholders);
		Assert.Null(escaped.Problem);
		Assert.Equal(text, escaped.Fill(null));
	}

	[Fact]
	public void EscapeText_DoublesOnlyTheBracesThatWouldBeRead() =>
		Assert.Equal(
			"docker ps --filter name={{{{x}} --format '{{.Names}}'",
			CommandTemplate.EscapeText("docker ps --filter name={{x}} --format '{{.Names}}'"));

	// Every string over the characters that matter, so the escape and the parser cannot drift apart unnoticed.
	[Fact]
	public void EscapeText_RoundTripsAnyMixOfBracesNamesAndColons()
	{
		const string Alphabet = "{{}}::a.-_ \n";
		Random random = new(20261018);
		for (int round = 0; round < 5000; round++)
		{
			char[] text = new char[random.Next(0, 24)];
			for (int index = 0; index < text.Length; index++)
			{
				text[index] = Alphabet[random.Next(Alphabet.Length)];
			}

			string source = new(text);
			CommandTemplate escaped = CommandTemplate.Parse(CommandTemplate.EscapeText(source));
			Assert.False(escaped.HasPlaceholders, source);
			Assert.Null(escaped.Problem);
			Assert.Equal(source, escaped.Fill(null));
		}
	}

	[Fact]
	public void PrintingAPlaceholderOrAPart_NeverShowsItsDefault()
	{
		CommandTemplate template = CommandTemplate.Parse("curl -H 'Authorization: {{token:s3cr3t}}' api");

		Assert.DoesNotContain("s3cr3t", template.Placeholders[0].ToString(), StringComparison.Ordinal);
		Assert.All(template.Parts, part => Assert.DoesNotContain("s3cr3t", part.ToString(), StringComparison.Ordinal));
	}

	private static Dictionary<string, string> Values(params (string Name, string Value)[] values) =>
		values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);
}
