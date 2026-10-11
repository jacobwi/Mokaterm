using Mokaterm.Modules.Local.Shells;

namespace Mokaterm.Modules.Local.Tests;

public sealed class LocalEnvironmentVariablesTests
{
	[Fact]
	public void TryParse_ReadsOneVariablePerLine_SkippingBlanksAndComments()
	{
		Assert.True(LocalEnvironmentVariables.TryParse("EDITOR = hx\r\n\r\n# a note\n  PAGER=less -R\nREMOVED=", out IReadOnlyList<KeyValuePair<string, string>> variables, out string? error));

		// The name is trimmed and the value is not: what follows the equals sign is the value.
		KeyValuePair<string, string>[] expected = [new("EDITOR", " hx"), new("PAGER", "less -R"), new("REMOVED", "")];
		Assert.Null(error);
		Assert.Equal(expected, variables);
	}

	[Fact]
	public void TryParse_ALaterLineForTheSameName_Wins()
	{
		Assert.True(LocalEnvironmentVariables.TryParse("A=1\na=2", out IReadOnlyList<KeyValuePair<string, string>> variables, out _));

		KeyValuePair<string, string> variable = Assert.Single(variables);
		Assert.Equal("a", variable.Key);
		Assert.Equal("2", variable.Value);
	}

	[Fact]
	public void TryParse_AValueMayHoldEqualsSigns()
	{
		Assert.True(LocalEnvironmentVariables.TryParse("QUERY=a=1&b=2", out IReadOnlyList<KeyValuePair<string, string>> variables, out _));

		Assert.Equal("a=1&b=2", Assert.Single(variables).Value);
	}

	[Theory]
	[InlineData("A=1\nNO_VALUE", "Line 2 needs a value after an equals sign, as in NAME=value.")]
	[InlineData("=orphan", "Line 1 has no name before its equals sign.")]
	public void TryParse_NamesTheLineThatCannotBeRead(string text, string expected)
	{
		Assert.False(LocalEnvironmentVariables.TryParse(text, out _, out string? error));

		Assert.Equal(expected, error);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" \n ")]
	public void TryParse_Nothing_IsNoVariables(string? text)
	{
		Assert.True(LocalEnvironmentVariables.TryParse(text, out IReadOnlyList<KeyValuePair<string, string>> variables, out _));

		Assert.Empty(variables);
	}
}
