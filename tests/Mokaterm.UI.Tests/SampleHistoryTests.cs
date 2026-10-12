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
	public async Task ASparkline_TakesItAsItsValues()
	{
		SampleHistory history = new(4);
		history.Add(0);
		history.Add(10);

		string before = await StaticRender.RenderAsync<Sparkline>(_ => { }, new() { ["Values"] = history, ["Max"] = 10.0, ["Slots"] = history.Capacity })
			.WaitAsync(Wait, TestContext.Current.CancellationToken);

		Assert.Contains($"points=\"{Sparkline.Points([0, 10], 10, 4)}\"", before, StringComparison.Ordinal);
	}
}
