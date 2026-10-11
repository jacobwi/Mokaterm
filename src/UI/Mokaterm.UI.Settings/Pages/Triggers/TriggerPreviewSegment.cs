using Mokaterm.UI.Terminal.Triggers;

namespace Mokaterm.UI.Settings.Pages.Triggers;

/// <summary>A run of a sample line drawn in one color, or in the plain text color when <see cref="Color"/> is null.</summary>
public sealed record TriggerPreviewSegment(string Text, string? Color)
{
	/// <summary>
	/// <paramref name="text"/> cut into runs of one color each. Layers paint in order, so where two rules match the same
	/// characters the later one wins, as it does in a terminal. Ranges past the end of the text are ignored.
	/// </summary>
	public static IReadOnlyList<TriggerPreviewSegment> Split(string text, IEnumerable<TriggerLayer> layers)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(layers);
		string?[] colors = new string?[text.Length];
		foreach (TriggerLayer layer in layers)
		{
			if (layer.Color is not { } color)
			{
				continue;
			}

			foreach (TriggerMatchRange range in layer.Matches)
			{
				int start = Math.Clamp(range.Start, 0, text.Length);
				int end = (int)Math.Clamp((long)range.Start + range.Length, start, text.Length);
				Array.Fill(colors, color, start, end - start);
			}
		}

		List<TriggerPreviewSegment> segments = [];
		int runStart = 0;
		for (int i = 1; i <= text.Length; i++)
		{
			if (i == text.Length || !string.Equals(colors[i], colors[runStart], StringComparison.Ordinal))
			{
				segments.Add(new TriggerPreviewSegment(text[runStart..i], colors[runStart]));
				runStart = i;
			}
		}

		return segments;
	}
}
