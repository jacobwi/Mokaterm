using Mokaterm.Abstractions.Connections;
using Mokaterm.UI.Presentation;

namespace Mokaterm.UI.Tests;

public sealed class HostAccentTests
{
	[Fact]
	public void For_KeepsTheColourTheHostWasGiven() =>
		Assert.Equal("#4488ff", HostAccent.For(Host("10.10.2.3", color: " #48f ")));

	[Fact]
	public void For_IgnoresAColourThatIsNotOne() =>
		Assert.Equal(HostAccent.For("10.10.2.3"), HostAccent.For(Host("10.10.2.3", color: "nonsense")));

	[Theory]
	[InlineData("build-01.example.com", "BUILD-01.EXAMPLE.COM")]
	[InlineData("10.10.2.3", "10.10.2.3")]
	public void For_GivesOneAddressTheSameColourEveryTime(string address, string same) =>
		Assert.Equal(HostAccent.For(address), HostAccent.For(same));

	[Fact]
	public void For_StaysInsideThePaletteTheStylesheetDefines()
	{
		HashSet<string> used = [];
		for (int i = 0; i < 500; i++)
		{
			string colour = HostAccent.For($"host-{i}.example.com");
			Assert.Matches("^var\\(--mt-host-[1-8]\\)$", colour);
			used.Add(colour);
		}

		// Eight colours over five hundred addresses: a palette that collapses to one or two would defeat the point.
		Assert.Equal(8, used.Count);
	}

	private static HostProfile Host(string address, string? color = null) =>
		new() { Id = Guid.NewGuid(), Address = address, Color = color };
}
