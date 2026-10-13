using System.Text.Encodings.Web;

namespace Mokaterm.Tests.Shared;

/// <summary>
/// Reads the tooltips Hint and IconButton render: Moka.Red's trigger wrapper, the control inside it, then the popup with
/// the text. Static rendering never hovers, so a test checks what is there to show and what it is attached to.
/// </summary>
internal static class Tooltips
{
	private const string TriggerStart = "<span class=\"moka-tooltip-trigger";

	private const string HintClass = "class=\"mt-hint\"";

	/// <summary>
	/// The control a tooltip saying <paramref name="text"/> belongs to: the markup between its trigger's opening tag and the
	/// popup. Fails the test when no tooltip says it.
	/// </summary>
	public static string ControlFor(string html, string text)
	{
		int popup = PopupStart(html, text);
		int trigger = html.LastIndexOf(TriggerStart, popup, StringComparison.Ordinal);
		Assert.True(trigger >= 0, $"The tooltip \"{text}\" has no trigger.");
		int controlStart = html.IndexOf('>', trigger) + 1;
		return html[controlStart..popup];
	}

	/// <summary>The popup's opening tag for the tooltip saying <paramref name="text"/>, which carries its position class.</summary>
	public static string PopupFor(string html, string text)
	{
		int popup = PopupStart(html, text);
		return html[popup..(html.IndexOf('>', popup) + 1)];
	}

	/// <summary>Every tooltip text on the page, in order.</summary>
	public static List<string> Texts(string html)
	{
		List<string> texts = [];
		int at = 0;
		while ((at = html.IndexOf(HintClass, at, StringComparison.Ordinal)) >= 0)
		{
			int start = html.IndexOf('>', at) + 1;
			int end = html.IndexOf("</span>", start, StringComparison.Ordinal);
			texts.Add(System.Net.WebUtility.HtmlDecode(html[start..end]));
			at = end;
		}

		return texts;
	}

	private static int PopupStart(string html, string text)
	{
		string body = $">{HtmlEncoder.Default.Encode(text)}</span>";
		int at = 0;
		while ((at = html.IndexOf(HintClass, at, StringComparison.Ordinal)) >= 0)
		{
			int start = html.IndexOf('>', at);
			if (string.CompareOrdinal(html, start, body, 0, body.Length) == 0)
			{
				int popup = html.LastIndexOf("<span class=\"moka-tooltip-popup", at, StringComparison.Ordinal);
				Assert.True(popup >= 0, $"The text \"{text}\" is not in a tooltip.");
				return popup;
			}

			at = start;
		}

		Assert.Fail($"No tooltip says \"{text}\".");
		return -1;
	}
}
