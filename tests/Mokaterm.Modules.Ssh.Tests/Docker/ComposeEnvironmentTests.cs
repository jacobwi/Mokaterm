using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>.env files the way compose reads them, the env files a project names, and the variables nothing sets.</summary>
public sealed class ComposeEnvironmentTests
{
	[Fact]
	public void DotEnv_ReadsEveryFormComposeTakes()
	{
		const string Text = """
			# Shared by every service.
			PLAIN=value
			export EXPORTED=yes
			SPACED = around the equals
			EMPTY=
			NAME_ONLY
			SINGLE='keeps $HOME and \n as they are'
			DOUBLE="tab\there, quote \" inside"
			INLINE=value # a comment after a blank
			HASH=pass#word
			QUOTED_HASH="has # inside" # and a comment
			""";

		IReadOnlyList<DotEnvVariable> variables = DotEnvParser.Parse(Text);

		Assert.Equal(
			[
				("PLAIN", "value"),
				("EXPORTED", "yes"),
				("SPACED", "around the equals"),
				("EMPTY", ""),
				("NAME_ONLY", null),
				("SINGLE", "keeps $HOME and \\n as they are"),
				("DOUBLE", "tab\there, quote \" inside"),
				("INLINE", "value"),
				("HASH", "pass#word"),
				("QUOTED_HASH", "has # inside"),
			],
			variables.Select(variable => (variable.Name, variable.Value)));
		Assert.Equal(2, variables[0].Line);
	}

	[Fact]
	public void DotEnv_AQuotedValue_MayRunOverSeveralLines()
	{
		const string Text = "KEY=\"-----BEGIN KEY-----\nabc\n-----END KEY-----\"\nAFTER=1\n";

		IReadOnlyList<DotEnvVariable> variables = DotEnvParser.Parse(Text);

		Assert.Equal("-----BEGIN KEY-----\nabc\n-----END KEY-----", variables[0].Value);
		Assert.Equal(("AFTER", "1", 4), (variables[1].Name, variables[1].Value, variables[1].Line));
	}

	[Fact]
	public void DotEnv_AQuoteThatNeverCloses_IsLeftOut_NotTheRestOfTheFile()
	{
		IReadOnlyList<DotEnvVariable> variables = DotEnvParser.Parse("BROKEN=\"no end\nNEXT=1");

		Assert.Equal([("NEXT", "1")], variables.Select(variable => (variable.Name, variable.Value)));
	}

	[Fact]
	public void DotEnv_AByteOrderMarkAndCrLf_AreNotPartOfANameOrValue()
	{
		IReadOnlyList<DotEnvVariable> variables = DotEnvParser.Parse("\uFEFFFIRST=1\r\nSECOND=2\r\n");

		Assert.Equal([("FIRST", "1"), ("SECOND", "2")], variables.Select(variable => (variable.Name, variable.Value)));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("# only a comment\n\n")]
	[InlineData("=no name\nwith space=x")]
	public void DotEnv_NothingToRead(string? text) => Assert.Empty(DotEnvParser.Parse(text));

	[Fact]
	public void DotEnv_ToString_NeverPrintsAValue()
	{
		DotEnvVariable variable = DotEnvParser.Parse("TOKEN=abc123")[0];

		Assert.DoesNotContain("abc123", variable.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void EnvFilesFromConfigJson_TakesEveryForm()
	{
		const string Json = """
			{"name":"shop","services":{
			  "web":{"env_file":["/srv/shop/web.env","/srv/shop/common.env"]},
			  "api":{"env_file":[{"path":"/srv/shop/api.env","required":true},{"path":"/srv/shop/common.env","required":false}]},
			  "db":{"env_file":"/srv/shop/db.env"},
			  "cache":{"image":"redis"}
			}}
			""";

		Assert.Equal(
			["/srv/shop/web.env", "/srv/shop/common.env", "/srv/shop/api.env", "/srv/shop/db.env"],
			ComposeEnvironment.EnvFilesFromConfigJson(Json));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("not json")]
	[InlineData("{\"services\":[]}")]
	public void EnvFilesFromConfigJson_NothingUsable(string? json) => Assert.Empty(ComposeEnvironment.EnvFilesFromConfigJson(json));

	[Fact]
	public void EnvFilesFromYaml_ReadsInlineFlowAndBlockForms_RelativeToTheFile()
	{
		const string Yaml = """
			services:
			  web:
			    image: nginx
			    env_file: web.env   # inline
			  api:
			    env_file: [api.env, "./common.env"]
			  worker:
			    env_file:
			      - path: ./worker.env
			        required: false
			      - /etc/shop/secrets.env
			      # - commented.env
			    environment:
			      - NOT_A_FILE=1
			  db:
			    env_file:
			      - ${ENV_DIR}/db.env
			      - '../shared/db.env'
			""";

		Assert.Equal(
			["/srv/shop/web.env", "/srv/shop/api.env", "/srv/shop/common.env", "/srv/shop/worker.env", "/etc/shop/secrets.env", "/srv/shared/db.env"],
			ComposeEnvironment.EnvFilesFromYaml(Yaml, "/srv/shop/compose.yaml"));
	}

	[Fact]
	public void UnsetVariables_ReadEveryWayComposeWarns()
	{
		const string Warnings = """
			time="2026-10-11T07:00:00Z" level=warning msg="The \"SMTP_PASSWORD\" variable is not set. Defaulting to a blank string."
			WARN[0000] The "API_KEY" variable is not set. Defaulting to a blank string.
			WARNING: The OLD_STYLE variable is not set. Defaulting to a blank string.
			time="2026-10-11T07:00:00Z" level=warning msg="The \"SMTP_PASSWORD\" variable is not set. Defaulting to a blank string."
			time="2026-10-11T07:00:00Z" level=warning msg="/srv/shop/compose.yaml: `version` is obsolete"
			""";

		Assert.Equal(["SMTP_PASSWORD", "API_KEY", "OLD_STYLE"], ComposeEnvironment.UnsetVariables(Warnings));
		Assert.Empty(ComposeEnvironment.UnsetVariables(null));
	}
}
