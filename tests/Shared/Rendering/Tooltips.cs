using System.Net;

namespace Mokaterm.Tests.Shared;

/// <summary>
/// Reads Moka.Red's tooltips as they render: the trigger wrapper, the control inside it, then the popup with the text.
/// Hint and IconButton draw them, and so do Moka's own controls (a status bar item, a tab, a dialog's close button), all
/// as plain text in the popup. Static rendering never hovers, so a test checks what is there to show and what it is
/// attached to.
/// </summary>
internal static class Tooltips
{
	private const string TriggerStart = "<span class=\"moka-tooltip-trigger";

	private const string PopupTag = "<span class=\"moka-tooltip-popup";

	private const string PositionPrefix = "moka-tooltip-popup--";

	/// <summary>
	/// The control a tooltip saying <paramref name="text"/> belongs to: the markup between its trigger's opening tag and the
	/// popup. Fails the test when no tooltip says it.
	/// </summary>
	public static string ControlFor(string html, string text)
	{
		int popup = Find(html, text);
		int trigger = html.LastIndexOf(TriggerStart, popup, StringComparison.Ordinal);
		Assert.True(trigger >= 0, $"The tooltip \"{text}\" has no trigger.");
		int controlStart = html.IndexOf('>', trigger) + 1;
		return html[controlStart..popup];
	}

	/// <summary>The opening tag of the trigger around the tooltip saying <paramref name="text"/>.</summary>
	public static string TriggerFor(string html, string text)
	{
		int popup = Find(html, text);
		int trigger = html.LastIndexOf(TriggerStart, popup, StringComparison.Ordinal);
		Assert.True(trigger >= 0, $"The tooltip \"{text}\" has no trigger.");
		return html[trigger..(html.IndexOf('>', trigger) + 1)];
	}

	/// <summary>The popup's opening tag for the tooltip saying <paramref name="text"/>, which carries its classes and style.</summary>
	public static string PopupFor(string html, string text)
	{
		int popup = Find(html, text);
		return html[popup..(html.IndexOf('>', popup) + 1)];
	}

	/// <summary>Where the tooltip saying <paramref name="text"/> opens, as its popup's class names it: "bottom", "top-end".</summary>
	public static string PositionOf(string html, string text)
	{
		string tag = PopupFor(html, text);
		int start = tag.IndexOf("class=\"", StringComparison.Ordinal) + "class=\"".Length;
		string classes = tag[start..tag.IndexOf('"', start)];
		return Assert.Single(
			classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Where(name => name.StartsWith(PositionPrefix, StringComparison.Ordinal) && name != PositionPrefix + "wrap")
				.Select(name => name[PositionPrefix.Length..]));
	}

	/// <summary>Every tooltip text on the page, in order.</summary>
	public static List<string> Texts(string html) => [.. Popups(html).Select(popup => popup.Text)];

	private static int Find(string html, string text)
	{
		foreach ((int start, string said) in Popups(html))
		{
			if (said == text)
			{
				return start;
			}
		}

		Assert.Fail($"No tooltip says \"{text}\".");
		return -1;
	}

	// Each popup with its text. A popup holds plain text: Hint passes its text through, and so does every Moka control.
	private static IEnumerable<(int Start, string Text)> Popups(string html)
	{
		int at = 0;
		while ((at = html.IndexOf(PopupTag, at, StringComparison.Ordinal)) >= 0)
		{
			int start = html.IndexOf('>', at) + 1;
			int end = html.IndexOf("</span>", start, StringComparison.Ordinal);
			yield return (at, WebUtility.HtmlDecode(html[start..end].Trim()));
			at = end;
		}
	}
}
