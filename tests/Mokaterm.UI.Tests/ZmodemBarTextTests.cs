using System.Globalization;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Terminal.Internal;

namespace Mokaterm.UI.Tests;

/// <summary>The words the terminal's rz/sz bar shows: offers, the request for files, and the progress line.</summary>
public sealed class ZmodemBarTextTests
{
	[Fact]
	public void Offer_WithAStatedSize_ShowsIt() =>
		AssertFormatted("report.txt (11.7 KB)", () => ZmodemBarText.Offer(new ZmodemFileOffer { FileName = "report.txt", Length = 12_000 }));

	[Fact]
	public void Offer_WithoutAStatedSize_ShowsOnlyTheName() =>
		AssertFormatted("report.txt", () => ZmodemBarText.Offer(new ZmodemFileOffer { FileName = "report.txt" }));

	[Fact]
	public void WaitingForFiles_SaysWhatTheSessionWaitsFor() =>
		AssertFormatted("The session waits to receive files. Choose what to send.", ZmodemBarText.WaitingForFiles);

	[Fact]
	public void Progress_WithASize_ShowsPercentBytesRateAndTimeLeft() =>
		AssertFormatted(
			"50% · 500 B / 1000 B · 250 B/s · 00:02 left",
			() => ZmodemBarText.Progress(new ZmodemProgress("report.txt", Receiving: true, TransferredBytes: 500, TotalBytes: 1000, BytesPerSecond: 250)));

	[Fact]
	public void Progress_AtTheEnd_HasNoTimeLeft() =>
		AssertFormatted(
			"100% · 1000 B / 1000 B · 250 B/s",
			() => ZmodemBarText.Progress(new ZmodemProgress("report.txt", Receiving: false, TransferredBytes: 1000, TotalBytes: 1000, BytesPerSecond: 250)));

	[Fact]
	public void Progress_WithoutARate_ShowsOnlyCounts() =>
		AssertFormatted(
			"10% · 100 B / 1000 B",
			() => ZmodemBarText.Progress(new ZmodemProgress("report.txt", Receiving: true, TransferredBytes: 100, TotalBytes: 1000, BytesPerSecond: 0)));

	[Fact]
	public void Progress_WithoutASize_ShowsBytesAndRate() =>
		AssertFormatted(
			"4 KB · 1 KB/s",
			() => ZmodemBarText.Progress(new ZmodemProgress("stream.dump", Receiving: true, TransferredBytes: 4096, TotalBytes: null, BytesPerSecond: 1024)));

	// The formatters follow the current culture; the expected strings are written in the invariant one.
	private static void AssertFormatted(string expected, Func<string> format)
	{
		CultureInfo before = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		try
		{
			Assert.Equal(expected, format());
		}
		finally
		{
			CultureInfo.CurrentCulture = before;
		}
	}
}
