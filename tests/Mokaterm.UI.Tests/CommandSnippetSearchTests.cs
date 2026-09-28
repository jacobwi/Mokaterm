using Mokaterm.Abstractions.Commands;
using Mokaterm.UI.Common.Commands;

namespace Mokaterm.UI.Tests;

public sealed class CommandSnippetSearchTests
{
	private static readonly CommandSnippet Restart = Snippet("Restart nginx", "sudo systemctl restart nginx", "ops", "web");
	private static readonly CommandSnippet Logs = Snippet("Tail the log", "tail -f /var/log/syslog", "ops");
	private static readonly CommandSnippet Disk = Snippet("Disk usage", "df -h");

	[Fact]
	public void Filter_BlankQuery_KeepsEverythingInOrder()
	{
		IReadOnlyList<CommandSnippet> all = [Restart, Logs, Disk];

		Assert.Same(all, CommandSnippetSearch.Filter(all, "   "));
		Assert.Same(all, CommandSnippetSearch.Filter(all, null));
	}

	[Theory]
	[InlineData("nginx", "Restart nginx")]
	[InlineData("NGINX", "Restart nginx")]
	[InlineData("systemctl", "Restart nginx")]
	[InlineData("web", "Restart nginx")]
	[InlineData("syslog", "Tail the log")]
	[InlineData("df", "Disk usage")]
	public void Filter_MatchesNameCommandAndTags(string query, string expected)
	{
		CommandSnippet match = Assert.Single(CommandSnippetSearch.Filter([Restart, Logs, Disk], query));

		Assert.Equal(expected, match.Name);
	}

	[Fact]
	public void Filter_EveryWordMustMatchSomewhere()
	{
		Assert.Single(CommandSnippetSearch.Filter([Restart, Logs, Disk], "ops nginx"));
		Assert.Empty(CommandSnippetSearch.Filter([Restart, Logs, Disk], "ops nginx missing"));
		Assert.Equal(2, CommandSnippetSearch.Filter([Restart, Logs, Disk], "ops").Count);
	}

	[Fact]
	public void Filter_KeepsTheOrderItWasGiven()
	{
		IReadOnlyList<CommandSnippet> filtered = CommandSnippetSearch.Filter([Logs, Restart], "o");

		Assert.Equal(["Tail the log", "Restart nginx"], filtered.Select(snippet => snippet.Name));
	}

	[Theory]
	[InlineData(CommandSnippetScope.Connection, "Login")]
	[InlineData(CommandSnippetScope.Host, "Machine")]
	[InlineData(CommandSnippetScope.Global, "Global")]
	public void ScopeName_IsShortEnoughForATag(CommandSnippetScope scope, string expected) =>
		Assert.Equal(expected, CommandSnippetSearch.ScopeName(scope));

	private static CommandSnippet Snippet(string name, string command, params string[] tags) => new()
	{
		Id = Guid.NewGuid(),
		Name = name,
		Command = command,
		Tags = tags,
	};
}
