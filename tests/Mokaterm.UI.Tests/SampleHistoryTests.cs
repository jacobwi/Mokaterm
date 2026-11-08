using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Tests;

/// <summary>The bounded history a sparkline draws from.</summary>
public sealed class SampleHistoryTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

	[Fact]
	public void KeepsTheNewest_OldestFirst()
	{
		SampleHistory history = new(3);
		Assert.Null(history.Latest);

		foreach (double value in (double[])[1, 2, 3, 4, 5])
		{
			history.Add(value);
		}

		Assert.Equal([3.0, 4.0, 5.0], history);
		Assert.Equal(3, history.Count);
		Assert.Equal(3, history[0]);
		Assert.Equal(5, history.Latest);
		Assert.Throws<ArgumentOutOfRangeException>(() => history[3]);

		history.Clear();
		Assert.Empty(history);
		history.Add(9);
		Assert.Equal([9.0], history);
	}

	[Fact]
	public void RefusesNoRoom() => Assert.Throws<ArgumentOutOfRangeException>(() => new SampleHistory(0));

	[Fact]
	public void AWindow_PadsTheFrontWithGaps_AndKeepsTheValuesAsTheyAre()
	{
		SampleHistory history = new(4);
		history.Add(3);
		history.Add(double.NaN);

		double[] window = SampleHistory.Window(history, history.Capacity);

		Assert.Equal(4, window.Length);
		Assert.True(double.IsNaN(window[0]) && double.IsNaN(window[1]));
		Assert.Equal(3, window[2]);
		Assert.True(double.IsNaN(window[3]));

		// A window never cuts samples: more than the slots are all there.
		Assert.Equal([1d, 2d, 3d], SampleHistory.Window([1, 2, 3], 2));
		Assert.Empty(SampleHistory.Window([], 0));
	}

	[Fact]
	public async Task ASparkline_TakesItAsItsValues()
	{
		SampleHistory history = new(4);
		history.Add(0);
		history.Add(10);

		string html = await StaticRender.RenderAsync<Sparkline>(_ => { }, new() { ["Values"] = history, ["Max"] = 10.0, ["Slots"] = history.Capacity })
			.WaitAsync(Wait, TestContext.Current.CancellationToken);

		// The two samples sit in the right half of the four slots: from x 80 of Moka's 120 wide box to its right edge.
		Assert.Contains("moka-sparkline-line", html, StringComparison.Ordinal);
		Assert.Contains("M 80.00", html, StringComparison.Ordinal);
		Assert.Contains("L 120.00", html, StringComparison.Ordinal);
	}
}
