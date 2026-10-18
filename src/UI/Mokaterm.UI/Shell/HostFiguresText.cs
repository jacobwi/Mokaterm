using System.Globalization;
using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.UI.Shell;

/// <summary>
/// What the status bar says about the active session's machine: three short figures for the bar, and the sentences
/// behind them for its tooltip.
/// </summary>
internal static class HostFiguresText
{
	/// <summary>
	/// CPU, memory and root disk use, in that order, each coloured by <see cref="UsageBar.LevelOf"/>. A figure the read
	/// did not give is left out: CPU use is a difference, so it joins from the second read on.
	/// </summary>
	public static IReadOnlyList<HostFigure> Figures(HostFigures figures)
	{
		ArgumentNullException.ThrowIfNull(figures);
		List<HostFigure> shown = [];
		if (figures.CpuPercent is { } cpu)
		{
			shown.Add(Figure("CPU", cpu));
		}

		if (figures.MemoryPercent is { } memory)
		{
			shown.Add(Figure("MEM", memory));
		}

		if (figures.RootDisk is { } disk)
		{
			shown.Add(Figure("DISK", disk.UsedPercent));
		}

		return shown;
	}

	/// <summary>
	/// The figures in plain words: CPU and load, memory and swap, the root disk and uptime, then why they are old when the
	/// last read failed, and what a click does when it does something.
	/// </summary>
	/// <param name="toolTitle">The title of the session tool a click opens, or null when the item does nothing.</param>
	public static string Tooltip(HostMonitorState state, string? toolTitle)
	{
		ArgumentNullException.ThrowIfNull(state);
		if (state.Figures is not { } figures)
		{
			return "";
		}

		List<string> sentences = [CpuSentence(figures)];
		if (MemorySentence(figures) is { } memory)
		{
			sentences.Add(memory);
		}

		if (figures.RootDisk is { } disk)
		{
			sentences.Add($"Disk {disk.MountPoint}: {DisplayFormat.Bytes(disk.UsedBytes)} of {DisplayFormat.Bytes(disk.TotalBytes)} used.");
		}

		if (figures.Uptime is { } uptime)
		{
			sentences.Add($"Up {DisplayFormat.Span(uptime)}.");
		}

		if (state.IsStale)
		{
			string readAt = figures.ReadAt.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);
			sentences.Add(state.Problem is { Length: > 0 } problem
				? $"These figures are from {readAt}; the read after them failed. {problem}"
				: $"These figures are from {readAt}; the read after them failed.");
		}

		if (toolTitle is { Length: > 0 } title)
		{
			sentences.Add($"Click to open the {title} tool.");
		}

		return string.Join(' ', sentences);
	}

	// The colour follows the number shown, so 74.6 reads as 75% in the warning colour rather than 75% in the plain one.
	private static HostFigure Figure(string label, double percent)
	{
		double shown = Shown(percent);
		return new(label, string.Create(CultureInfo.InvariantCulture, $"{shown:0}%"), UsageBar.LevelOf(shown));
	}

	private static double Shown(double percent) => Math.Round(Math.Clamp(percent, 0, 100), MidpointRounding.AwayFromZero);

	private static string CpuSentence(HostFigures figures)
	{
		string cpu = figures.CpuPercent is { } busy
			? string.Create(CultureInfo.CurrentCulture, $"CPU {Shown(busy):0}% busy")
			: "CPU use comes with the next read";
		return figures.Load1 is { } one && figures.Load5 is { } five && figures.Load15 is { } fifteen
			? string.Create(CultureInfo.CurrentCulture, $"{cpu}, load {one:0.00}, {five:0.00} and {fifteen:0.00} over 1, 5 and 15 minutes.")
			: cpu + ".";
	}

	private static string? MemorySentence(HostFigures figures)
	{
		if (figures is not { MemoryUsedBytes: { } used, MemoryTotalBytes: { } total })
		{
			return null;
		}

		string swap = figures is { SwapUsedBytes: { } swapUsed, SwapTotalBytes: { } swapTotal } && swapTotal > 0
			? $"swap {DisplayFormat.Bytes(swapUsed)} of {DisplayFormat.Bytes(swapTotal)}"
			: "no swap";
		return $"Memory: {DisplayFormat.Bytes(used)} of {DisplayFormat.Bytes(total)} used, {swap}.";
	}
}
