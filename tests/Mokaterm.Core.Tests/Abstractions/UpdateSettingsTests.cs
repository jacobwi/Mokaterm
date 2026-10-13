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
	public void Defaults_UseTheGitHubReleases_AndCheckAtStartup()
	{
		UpdateSettings defaults = new();

		Assert.Equal(UpdateSettings.GitHubReleases, defaults.Feed);
		Assert.Equal(UpdateSettings.GitHubReleases, UpdateSettings.NormalizeFeed(defaults.Feed));
		Assert.True(UpdateSettings.IsGitHubRepository(defaults.Feed));
		Assert.True(defaults.CheckAtStartup);
		Assert.Equal("updates", UpdateSettings.SectionKey);
	}

	[Theory]
	[InlineData("https://github.com/jacobwi/Mokaterm", true)]
	[InlineData("https://github.com/jacobwi/Mokaterm/", true)]
	[InlineData("  https://GitHub.com/acme/fork  ", true)]
	[InlineData("https://github.com/jacobwi", false)]
	[InlineData("https://github.com/jacobwi/Mokaterm/releases", false)]
	[InlineData("https://github.com/jacobwi/Mokaterm/releases/latest/download", false)]
	[InlineData("http://github.com/jacobwi/Mokaterm", false)]
	[InlineData("https://github.com.example.com/jacobwi/Mokaterm", false)]
	[InlineData("https://github.com/jacobwi/Mokaterm?tab=releases", false)]
	[InlineData("https://updates.example.com/mokaterm/", false)]
	[InlineData(@"C:\releases\mokaterm", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void IsGitHubRepository_OnlyARepositoryAddress(string? feed, bool expected) =>
		Assert.Equal(expected, UpdateSettings.IsGitHubRepository(feed));
}
