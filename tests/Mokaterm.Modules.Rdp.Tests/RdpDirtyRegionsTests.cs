using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Tests;

public sealed class RdpDirtyRegionsTests
{
	[Fact]
	public void Take_OnAFreshSet_ReturnsNothing()
	{
		RdpDirtyRegions regions = new();

		Assert.True(regions.IsEmpty);
		Assert.Empty(regions.Take());
	}

	[Fact]
	public void Add_IgnoresAnEmptyRegion()
	{
		RdpDirtyRegions regions = new();

		regions.Add(new RdpRegion(4, 4, 0, 10));
		regions.Add(new RdpRegion(4, 4, 10, 0));

		Assert.True(regions.IsEmpty);
	}

	[Fact]
	public void Add_IgnoresARegionAlreadyCovered()
	{
		RdpDirtyRegions regions = new();

		regions.Add(new RdpRegion(0, 0, 100, 100));
		regions.Add(new RdpRegion(10, 10, 20, 20));

		RdpRegion only = Assert.Single(regions.Take());
		Assert.Equal(new RdpRegion(0, 0, 100, 100), only);
	}

	[Fact]
	public void Add_KeepsRegionsApartWhileThereIsRoom()
	{
		RdpDirtyRegions regions = new();

		regions.Add(new RdpRegion(0, 0, 10, 10));
		regions.Add(new RdpRegion(500, 500, 10, 10));

		Assert.Equal(2, regions.Take().Count);
	}

	[Fact]
	public void Add_BeyondTheLimit_MergesTheCheapestPair()
	{
		RdpDirtyRegions regions = new();
		for (int i = 0; i < RdpDirtyRegions.MaxRegions; i++)
		{
			regions.Add(new RdpRegion(i * 1000, 0, 10, 10));
		}

		// This one sits right next to the last, so merging those two costs the least.
		regions.Add(new RdpRegion((RdpDirtyRegions.MaxRegions - 1) * 1000, 20, 10, 10));

		IReadOnlyList<RdpRegion> taken = regions.Take();
		Assert.Equal(RdpDirtyRegions.MaxRegions, taken.Count);
		Assert.Contains(new RdpRegion((RdpDirtyRegions.MaxRegions - 1) * 1000, 0, 10, 30), taken);
		Assert.Contains(new RdpRegion(0, 0, 10, 10), taken);
	}

	[Fact]
	public void Add_NeverKeepsMoreThanTheLimit()
	{
		RdpDirtyRegions regions = new();
		for (int i = 0; i < 50; i++)
		{
			regions.Add(new RdpRegion(i * 37, i * 11, 5, 5));
		}

		Assert.Equal(RdpDirtyRegions.MaxRegions, regions.Take().Count);
	}

	[Fact]
	public void Take_StartsOver()
	{
		RdpDirtyRegions regions = new();
		regions.Add(new RdpRegion(0, 0, 10, 10));

		Assert.Single(regions.Take());
		Assert.True(regions.IsEmpty);
		Assert.Empty(regions.Take());
	}

	[Fact]
	public void Clear_DropsWhatPiledUp()
	{
		RdpDirtyRegions regions = new();
		regions.Add(new RdpRegion(0, 0, 10, 10));

		regions.Clear();

		Assert.True(regions.IsEmpty);
	}

	[Fact]
	public void Union_CoversBoth()
	{
		RdpRegion union = new RdpRegion(10, 10, 5, 5).Union(new RdpRegion(100, 0, 5, 20));

		Assert.Equal(new RdpRegion(10, 0, 95, 20), union);
	}

	[Fact]
	public void Union_WithAnEmptyRegion_ChangesNothing()
	{
		RdpRegion region = new(10, 10, 5, 5);

		Assert.Equal(region, region.Union(default));
		Assert.Equal(region, default(RdpRegion).Union(region));
	}

	[Theory]
	[InlineData(-5, -5, 20, 20, 0, 0, 15, 15)]
	[InlineData(90, 90, 40, 40, 90, 90, 10, 10)]
	[InlineData(200, 0, 10, 10, 0, 0, 0, 0)]
	public void Clip_KeepsWhatIsInsideTheDesktop(int x, int y, int width, int height, int clippedX, int clippedY, int clippedWidth, int clippedHeight)
	{
		RdpRegion clipped = new RdpRegion(x, y, width, height).Clip(100, 100);

		Assert.Equal(new RdpRegion(clippedX, clippedY, clippedWidth, clippedHeight), clipped);
	}
}
