using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests.Search;

/// <summary>
/// The find patterns a query becomes: exact where a C-locale pattern can say what the query means, wider (and marked so)
/// where it cannot. Whether they really match what the query does is checked against find in <see cref="ServerSearchScriptTests"/>.
/// </summary>
public sealed class SearchNamePatternsTests
{
	private const char LongS = (char)0x017F;

	[Fact]
	public void AnEmptyPattern_IsEveryName()
	{
		SearchNamePatterns names = SearchNamePatterns.For(Query(""));

		Assert.Empty(names.Alternatives);
		Assert.True(names.AlternativesExact);
		Assert.Equal("*", names.Single);
		Assert.True(names.SingleExact);
	}

	[Theory]
	[InlineData("*.log", "*.log")]
	[InlineData("error.log", "*error.log*")]
	[InlineData("-rf", "*-rf*")]
	[InlineData("it's \"x\"", "*it's \"x\"*")]
	[InlineData("caf\u00e9", "*caf\u00e9*")]
	[InlineData("a[1]", "*a[[]1]*")]
	public void MatchingCase_IsTheTextItself(string pattern, string expected)
	{
		SearchNamePatterns names = SearchNamePatterns.For(Query(pattern) with { MatchCase = true });

		Assert.Equal([expected], names.Alternatives);
		Assert.True(names.AlternativesExact);
		Assert.Equal(expected, names.Single);
		Assert.True(names.SingleExact);
	}

	[Fact]
	public void IgnoringCase_PutsBothCasesOfAnAsciiLetterInABracket()
	{
		SearchNamePatterns names = SearchNamePatterns.For(Query("Log-2"));

		Assert.Equal(["*[Ll][oO][gG]-2*"], names.Alternatives);
		Assert.True(names.AlternativesExact);
		Assert.True(names.SingleExact);
	}

	[Fact]
	public void IgnoringCase_SpellsAnOtherCaseOutsideAscii_AsAnAlternative()
	{
		SearchNamePatterns names = SearchNamePatterns.For(Query("caf\u00e9"));

		Assert.Equal(["*[cC][aA][fF]\u00e9*", "*[cC][aA][fF]\u00c9*"], names.Alternatives);
		Assert.True(names.AlternativesExact);

		// A find without -printf takes one pattern, which then has to match more.
		Assert.Equal("*[cC][aA][fF]*", names.Single);
		Assert.False(names.SingleExact);
	}

	// The runtime decides whether the long s is an s to a glob; the patterns follow whatever it says.
	[Fact]
	public void IgnoringCase_InAGlob_FollowsTheRuntimesOwnFolding()
	{
		SearchNamePatterns names = SearchNamePatterns.For(Query("*.s"));
		bool folds = char.ToUpperInvariant(LongS) == 'S';

		Assert.Equal(folds ? ["*.[sS]", "*." + LongS] : ["*.[sS]"], names.Alternatives);
		Assert.True(names.AlternativesExact);
		Assert.Equal(!folds, names.SingleExact);
	}

	[Theory]
	[InlineData("error.???", "error.*")]
	[InlineData("a\\b", "*[aA]*[bB]*")]
	[InlineData("a/b", "*[aA]*[bB]*")]
	[InlineData("a\tb", "*[aA]*[bB]*")]
	[InlineData("a\ufffdb", "*[aA]*[bB]*")]
	public void WhatACLocalePatternCannotSay_BecomesAStar_AndIsNotExact(string pattern, string expected)
	{
		SearchNamePatterns names = SearchNamePatterns.For(Query(pattern) with { MatchCase = pattern.Contains('?', StringComparison.Ordinal) });

		Assert.Equal([expected], names.Alternatives);
		Assert.False(names.AlternativesExact);
		Assert.False(names.SingleExact);
	}

	[Fact]
	public void TooManyChoices_TurnEveryChoiceIntoAStar()
	{
		SearchNamePatterns names = SearchNamePatterns.For(Query("\u00e9\u00e9\u00e9\u00e9\u00e9"));

		Assert.Equal(["*"], names.Alternatives);
		Assert.False(names.AlternativesExact);
		Assert.Equal("*", names.Single);
	}

	[Fact]
	public void SixteenChoices_AreStillSpelledOut()
	{
		SearchNamePatterns names = SearchNamePatterns.For(Query("\u00e9\u00e9\u00e9\u00e9"));

		Assert.Equal(SearchNamePatterns.MaxAlternatives, names.Alternatives.Count);
		Assert.True(names.AlternativesExact);
	}

	[Fact]
	public void TheScriptStopsOnResultsOnlyWhenFindsMatchIsExact()
	{
		RemoteSearchQuery exact = Query("*.log") with { MaxResults = 7 };
		RemoteSearchQuery loose = Query("error.???") with { MaxResults = 7 };

		Assert.Equal(7, ServerSearch.ResultStop(exact, SearchNamePatterns.For(exact).AlternativesExact));
		Assert.Equal(0, ServerSearch.ResultStop(loose, SearchNamePatterns.For(loose).AlternativesExact));
		Assert.Equal(0, ServerSearch.ResultStop(exact with { Kinds = RemoteSearchKinds.None }, patternExact: true));
	}

	[Fact]
	public void TheArguments_KeepEveryPatternAWordOfItsOwn()
	{
		string[] arguments = ServerSearch.Arguments("/srv/site", Query("caf\u00e9") with { MaxDepth = 4, MaxFolders = 9, IncludeHidden = true, Kinds = RemoteSearchKinds.Files });

		Assert.Equal(
			["/srv/site", "4", "1", "f", "9", "25165824", "600", "*[cC][aA][fF]*", "0", "1000", "*[cC][aA][fF]\u00e9*", "*[cC][aA][fF]\u00c9*"],
			arguments);
	}

	private static RemoteSearchQuery Query(string pattern) => new() { Pattern = pattern };
}
