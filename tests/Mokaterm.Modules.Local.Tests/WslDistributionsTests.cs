using Mokaterm.Modules.Local.Shells;

namespace Mokaterm.Modules.Local.Tests;

public sealed class WslDistributionsTests
{
	[Fact]
	public void Select_PutsTheDefaultFirst_AndTheRestByName()
	{
		IReadOnlyList<WslDistribution> distributions = WslDistributions.Select(
		[
			("kali-linux", false),
			("Debian", false),
			("Ubuntu-24.04", true),
		]);

		Assert.Equal(
			[new WslDistribution("Ubuntu-24.04", true), new WslDistribution("Debian", false), new WslDistribution("kali-linux", false)],
			distributions);
	}

	[Fact]
	public void Select_LeavesOutDockersOwnAndUnnamedEntries()
	{
		IReadOnlyList<WslDistribution> distributions = WslDistributions.Select(
		[
			("docker-desktop", false),
			("docker-desktop-data", false),
			(null, false),
			("  ", false),
			("Ubuntu", false),
		]);

		Assert.Equal("Ubuntu", Assert.Single(distributions).Name);
	}

	[Fact]
	public void Select_KeepsOneOfTwoEntriesWithTheSameName()
	{
		IReadOnlyList<WslDistribution> distributions = WslDistributions.Select([("Ubuntu", false), (" ubuntu ", true)]);

		Assert.Equal(new WslDistribution("Ubuntu", false), Assert.Single(distributions));
	}
}
