using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.UI.Terminal.Internal;

/// <summary>
/// The words the terminal's ZMODEM bar shows: what the other side offers, what it waits for, and how far the moving file
/// is. Plain text in, plain text out, so the strings are checked without a browser.
/// </summary>
internal static class ZmodemBarText
{
	/// <summary><c>report.txt (12.4 kB)</c>, or without a stated size just <c>report.txt</c>.</summary>
	public static string Offer(ZmodemFileOffer offer) => offer.Length is { } length
		? $"{offer.FileName} ({DisplayFormat.Bytes(length)})"
		: offer.FileName;

	/// <summary>What the bar says while an <c>rz</c> on the other side waits for files.</summary>
	public static string WaitingForFiles() => "The session waits to receive files. Choose what to send.";

	/// <summary><c>45%, 120.5 KB/s, 00:04 left</c> when the size is known; <c>12.4 MB, 120.5 KB/s</c> when it is not.</summary>
	public static string Progress(ZmodemProgress progress)
	{
		string rate = DisplayFormat.Rate(progress.BytesPerSecond);
		if (progress.TotalBytes is not { } total || total <= 0)
		{
			return Join(DisplayFormat.Bytes(progress.TransferredBytes), rate);
		}

		int percent = (int)Math.Clamp(100d * progress.TransferredBytes / total, 0, 100);
		string left = percent < 100 && progress.BytesPerSecond > 0
			? DisplayFormat.Duration(TimeSpan.FromSeconds((total - progress.TransferredBytes) / progress.BytesPerSecond)) + " left"
			: "";
		return Join($"{percent}%", DisplayFormat.Bytes(progress.TransferredBytes) + " / " + DisplayFormat.Bytes(total), rate, left);
	}

	private static string Join(params string[] parts)
	{
		IEnumerable<string> filled = parts.Where(part => part.Length > 0);
		return string.Join(" · ", filled);
	}
}
