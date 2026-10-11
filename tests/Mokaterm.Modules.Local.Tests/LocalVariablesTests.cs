using Mokaterm.Modules.Local.Shells;

namespace Mokaterm.Modules.Local.Tests;

public sealed class LocalVariablesTests
{
	private static readonly Dictionary<string, string> Variables = new(StringComparer.OrdinalIgnoreCase)
	{
		["USERPROFILE"] = @"C:\Users\ada",
		["TOOLS"] = @"D:\tools",
	};

	[Theory]
	[InlineData(@"%USERPROFILE%\src", @"C:\Users\ada\src")]
	[InlineData(@"%userprofile%\src", @"C:\Users\ada\src")]
	[InlineData(@"%TOOLS%\%TOOLS%", @"D:\tools\D:\tools")]
	[InlineData(@"%MISSING%\x", @"%MISSING%\x")]
	[InlineData(@"100% sure", @"100% sure")]
	[InlineData(@"%%", @"%%")]
	[InlineData(@"50%%TOOLS%", @"50%D:\tools")]
	[InlineData(@"plain", @"plain")]
	public void Expand_ReplacesKnownNamesAndLeavesTheRest(string text, string expected)
	{
		Assert.Equal(expected, LocalVariables.Expand(text, name => Variables.TryGetValue(name, out string? value) ? value : null));
	}
}
