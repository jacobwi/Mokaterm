using System.Globalization;
using Mokaterm.Abstractions.Fleet;
using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The status bar's host figures: three short shares coloured by the rule every usage bar follows, and the sentences
/// behind them in the tooltip, stale ones saying from when.
/// </summary>
public sealed class HostFiguresTextTests
{
	private const long GiB = 1024L * 1024 * 1024;

	private static readonly DateTimeOffset ReadAt = new(2026, 10, 18, 9, 30, 5, TimeSpan.Zero);

	[Fact]
	public void Figures_AreCpuMemoryAndDisk_EachColouredByItsLevel()
	{
		IReadOnlyList<HostFigure> figures = HostFiguresText.Figures(Busy());

		Assert.Equal(
			[
				new HostFigure("CPU", "12%", UsageLevel.Normal),
				new HostFigure("MEM", "80%", UsageLevel.Warn),
				new HostFigure("DISK", "95%", UsageLevel.Full),
			],
			figures);
	}

	// CPU use is the difference between two reads, so the first read has none and the figure joins on the second.
	[Fact]
	public void Figures_BeforeTheSecondRead_LeaveCpuOut()
	{
		IReadOnlyList<HostFigure> figures = HostFiguresText.Figures(Busy() with { CpuPercent = null });

		Assert.Equal(["MEM", "DISK"], figures.Select(figure => figure.Label));
	}

	[Fact]
	public void Figures_AMachineThatReportsNothing_ShowsNothing() =>
		Assert.Empty(HostFiguresText.Figures(new HostFigures { ReadAt = ReadAt }));

	// The colour follows the number shown: 74.6 is shown as 75%, which is where the warning starts.
	[Theory]
	[InlineData(74.4, "74%", UsageLevel.Normal)]
	[InlineData(74.6, "75%", UsageLevel.Warn)]
	[InlineData(89.4, "89%", UsageLevel.Warn)]
	[InlineData(89.5, "90%", UsageLevel.Full)]
	[InlineData(100.2, "100%", UsageLevel.Full)]
	[InlineData(-0.3, "0%", UsageLevel.Normal)]
	public void Figures_ColourTheShareTheyShow(double cpu, string shown, UsageLevel level)
	{
		HostFigure figure = HostFiguresText.Figures(new HostFigures { ReadAt = ReadAt, CpuPercent = cpu }).Single();

		Assert.Equal(shown, figure.Value);
		Assert.Equal(level, figure.Level);
	}

	[Fact]
	public void Tooltip_SaysEveryFigureInWords_AndWhatAClickDoes() => InCulture(() =>
	{
		string text = HostFiguresText.Tooltip(new HostMonitorState { Figures = Busy() }, "System");

		Assert.Equal(
			"CPU 12% busy, load 0.42, 0.31 and 0.27 over 1, 5 and 15 minutes. Memory: 6.4 GB of 8 GB used, swap 1 GB of 2 GB. "
			+ "Disk /: 95 GB of 100 GB used. Up 4d 2h. Click to open the System tool.",
			text);
	});

	[Fact]
	public void Tooltip_WithoutSwapOrCpu_OrAToolToOpen() => InCulture(() =>
	{
		HostFigures figures = Busy() with { CpuPercent = null, SwapUsedBytes = 0, SwapTotalBytes = 0, Load1 = null };

		string text = HostFiguresText.Tooltip(new HostMonitorState { Figures = figures }, toolTitle: null);

		Assert.StartsWith("CPU use comes with the next read. Memory: 6.4 GB of 8 GB used, no swap.", text, StringComparison.Ordinal);
		Assert.DoesNotContain("Click", text, StringComparison.Ordinal);
	});

	[Fact]
	public void Tooltip_AfterAFailedRead_SaysTheFiguresAreOld_AndWhy() => InCulture(() =>
	{
		HostMonitorState state = new()
		{
			Figures = Busy(),
			FailedReads = 1,
			Problem = "The command did not finish within 10 seconds, so it was stopped.",
		};

		string text = HostFiguresText.Tooltip(state, "System");

		string at = ReadAt.ToLocalTime().ToString("T", CultureInfo.InvariantCulture);
		Assert.Contains($"These figures are from {at}; the read after them failed. The command did not finish within 10 seconds, so it was stopped.", text, StringComparison.Ordinal);
		Assert.EndsWith("Click to open the System tool.", text, StringComparison.Ordinal);
		Assert.True(state.IsStale);
		Assert.True(state.IsShown);
	});

	// Several failed reads in a row hide the figures; a session that cannot give them never shows them.
	[Fact]
	public void State_HidesFiguresTooOldOrNeverThere()
	{
		Assert.False(HostMonitorState.Waiting.IsShown);
		Assert.False(new HostMonitorState { Figures = Busy(), FailedReads = HostMonitorState.HideAfterFailures }.IsShown);
		Assert.True(new HostMonitorState { Figures = Busy(), FailedReads = HostMonitorState.HideAfterFailures - 1 }.IsShown);
		Assert.False(new HostMonitorState { Unavailable = "This login does not run scripts." }.IsShown);
		Assert.Equal("", HostFiguresText.Tooltip(HostMonitorState.Waiting, "System"));
	}

	private static HostFigures Busy() => new()
	{
		ReadAt = ReadAt,
		CpuPercent = 12.2,
		Load1 = 0.42,
		Load5 = 0.31,
		Load15 = 0.27,
		MemoryUsedBytes = (long)(6.4 * GiB),
		MemoryTotalBytes = 8 * GiB,
		SwapUsedBytes = 1 * GiB,
		SwapTotalBytes = 2 * GiB,
		RootDisk = new HostDiskUsage { MountPoint = "/", UsedBytes = 95 * GiB, TotalBytes = 100 * GiB, UsedPercent = 95 },
		Uptime = new TimeSpan(4, 2, 17, 0),
	};

	/// <summary>Runs <paramref name="test"/> with the invariant culture, so decimal points read the same on every machine.</summary>
	private static void InCulture(Action test)
	{
		CultureInfo before = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		try
		{
			test();
		}
		finally
		{
			CultureInfo.CurrentCulture = before;
		}
	}
}
