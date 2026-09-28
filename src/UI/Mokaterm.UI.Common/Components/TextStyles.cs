namespace Mokaterm.UI.Common.Components;

/// <summary>Token-based inline styles for text details that no Moka.Red parameter covers.</summary>
public static class TextStyles
{
	public const string Muted = "color:var(--moka-color-on-surface-variant)";

	/// <summary>Letter spacing and color for uppercase micro-labels; size, weight and case come from MokaText parameters.</summary>
	public const string MicroLabel = "letter-spacing:0.08em;color:var(--moka-color-on-surface-tertiary)";

	/// <summary>Lets a truncated inline MokaText ellipsize long values such as fingerprints and paths.</summary>
	public const string TruncateBox = "display:inline-block;max-width:280px;vertical-align:middle";
}
