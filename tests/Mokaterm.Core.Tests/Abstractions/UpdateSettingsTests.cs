using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class UpdateSettingsTests
{
	[Theory]
	[InlineData("https://updates.example.com/mokaterm/", "https://updates.example.com/mokaterm/")]
	[InlineData("  https://updates.example.com/mokaterm/  ", "https://updates.example.com/mokaterm/")]
	[InlineData("http://10.0.0.5:8080/feed", "http://10.0.0.5:8080/feed")]
	[InlineData("HTTPS://Updates.Example.com/Feed", "HTTPS://Updates.Example.com/Feed")]
	[InlineData(@"C:\releases\mokaterm", @"C:\releases\mokaterm")]
	[InlineData(@"\\build\releases\mokaterm", @"\\build\releases\mokaterm")]
	public void NormalizeFeed_KeepsAnAddressOrAFullPath(string feed, string expected) =>
		Assert.Equal(expected, UpdateSettings.NormalizeFeed(feed));

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("updates.example.com")]
	[InlineData("ftp://updates.example.com/feed")]
	[InlineData("file:///C:/releases")]
	[InlineData("https://")]
	[InlineData("releases")]
	[InlineData(@"..\releases")]
	[InlineData("https://updates.example.com/\nfeed")]
	public void NormalizeFeed_TurnsAnythingElseOff(string? feed) =>
		Assert.Equal("", UpdateSettings.NormalizeFeed(feed));

	[Fact]
	public void Defaults_HaveNoFeedAndCheckAtStartup()
	{
		UpdateSettings defaults = new();

		Assert.Equal("", defaults.Feed);
		Assert.True(defaults.CheckAtStartup);
		Assert.Equal("updates", UpdateSettings.SectionKey);
	}
}
