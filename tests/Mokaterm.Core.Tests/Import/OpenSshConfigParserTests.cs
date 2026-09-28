using Mokaterm.Abstractions.Import;
using Mokaterm.Core.Import;

namespace Mokaterm.Core.Tests.Import;

public sealed class OpenSshConfigParserTests
{
	[Fact]
	public void Parse_KeepsBlocksInOrderAndTreatsTheGlobalSectionAsAWildcard()
	{
		OpenSshConfigParseResult result = OpenSshConfigParser.Parse(
			"""
			User root
			Host web1
				HostName 10.0.0.1
			""",
			resolveInclude: null);

		Assert.Equal(2, result.Blocks.Count);
		Assert.Equal(["*"], result.Blocks[0].Patterns);
		Assert.Equal("User", result.Blocks[0].Settings[0].Key);
		Assert.Equal(["web1"], result.Blocks[1].Patterns);
	}

	[Theory]
	[InlineData("Host web1\n  HostName=10.0.0.1")]
	[InlineData("Host web1\n  HostName = 10.0.0.1")]
	[InlineData("Host\tweb1\n\t\tHostName\t10.0.0.1  ")]
	[InlineData("# a comment\n\nHost web1\n   hostname   10.0.0.1\n")]
	public void Parse_AcceptsTheSeparatorsAndWhitespaceOpenSshAccepts(string text)
	{
		OpenSshConfigParseResult result = OpenSshConfigParser.Parse(text, resolveInclude: null);

		OpenSshConfigBlock block = Assert.Single(result.Blocks);
		Assert.Equal(["web1"], block.Patterns);
		KeyValuePair<string, string> setting = Assert.Single(block.Settings);
		Assert.Equal("10.0.0.1", setting.Value);
	}

	[Fact]
	public void Parse_KeepsQuotedValuesWhole()
	{
		OpenSshConfigParseResult result = OpenSshConfigParser.Parse(
			"""
			Host "my box"
				ProxyCommand "C:\Program Files\bin\nc.exe" %h %p
			""",
			resolveInclude: null);

		OpenSshConfigBlock block = Assert.Single(result.Blocks);
		Assert.Equal(["my box"], block.Patterns);
		Assert.Equal(@"C:\Program Files\bin\nc.exe %h %p", block.Settings[0].Value);
	}

	[Fact]
	public void Parse_TreatsAHashAfterArgumentsAsAComment()
	{
		OpenSshConfigParseResult result = OpenSshConfigParser.Parse("Host web1\n Port 2222 # the tunnel", resolveInclude: null);

		Assert.Equal("2222", result.Blocks[0].Settings[0].Value);
	}

	[Fact]
	public void Parse_FollowsIncludesInPlace()
	{
		OpenSshConfigParseResult result = OpenSshConfigParser.Parse(
			"""
			Host web1
				Include extra
				Port 2222
			""",
			argument => argument == "extra" ? [new OpenSshConfigFile("extra", "Host db1\n HostName 10.0.0.9")] : []);

		Assert.Equal(2, result.Blocks.Count);
		Assert.Equal(["db1"], result.Blocks[0].Patterns);

		// The lines after the include still belong to the host block that held it.
		Assert.Equal(["web1"], result.Blocks[1].Patterns);
		Assert.Equal("Port", result.Blocks[1].Settings[0].Key);
	}

	[Fact]
	public void Parse_RefusesAnIncludeCycleInsteadOfLooping()
	{
		OpenSshConfigParseResult result = OpenSshConfigParser.Parse(
			"Include loop",
			_ => [new OpenSshConfigFile("loop", "Include loop\nHost web1\n HostName 10.0.0.1")]);

		Assert.Contains(result.Notes, note => note.Reason.Contains("includes itself", StringComparison.Ordinal));
		Assert.Single(result.Blocks);
	}

	[Fact]
	public void Parse_ReportsIncludesWhenTheyCannotBeFollowed()
	{
		OpenSshConfigParseResult result = OpenSshConfigParser.Parse("Include work/*", resolveInclude: null);

		ImportSkip note = Assert.Single(result.Notes);
		Assert.Equal("Include work/*", note.Name);
	}

	[Fact]
	public void Parse_LeavesMatchBlockSettingsAlone()
	{
		OpenSshConfigParseResult result = OpenSshConfigParser.Parse(
			"""
			Match host web1 exec "true"
				User deploy
			Host db1
				HostName 10.0.0.9
			""",
			resolveInclude: null);

		Assert.Contains(result.Notes, note => note.Name.StartsWith("Match ", StringComparison.Ordinal));
		OpenSshConfigBlock block = Assert.Single(result.Blocks);
		Assert.Equal(["db1"], block.Patterns);
	}

	[Theory]
	[InlineData("*", "anything", true)]
	[InlineData("*.example.com", "web.example.com", true)]
	[InlineData("*.example.com", "example.com", false)]
	[InlineData("web?", "web1", true)]
	[InlineData("web?", "web12", false)]
	[InlineData("WEB1", "web1", true)]
	public void Matches_FollowsTheHostPatternRules(string pattern, string host, bool expected) =>
		Assert.Equal(expected, OpenSshHostPattern.Matches(pattern, host));

	[Fact]
	public void Covers_LetsANegatedPatternWinOverAMatch()
	{
		Assert.False(OpenSshHostPattern.Covers(["*.example.com", "!secret.example.com"], "secret.example.com"));
		Assert.True(OpenSshHostPattern.Covers(["*.example.com", "!secret.example.com"], "web.example.com"));
	}

	[Theory]
	[InlineData("*", true)]
	[InlineData("!web1", true)]
	[InlineData("web?", true)]
	[InlineData("web1", false)]
	public void IsWildcard_SeparatesConcreteAliasesFromPatterns(string pattern, bool expected) =>
		Assert.Equal(expected, OpenSshHostPattern.IsWildcard(pattern));
}
