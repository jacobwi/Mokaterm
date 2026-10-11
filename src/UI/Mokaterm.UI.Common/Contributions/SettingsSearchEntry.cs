using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Common.Contributions;

/// <summary>A settings row the search can find, as its page's markup writes it.</summary>
public sealed record SettingsSearchEntry(string? Section, string Label, string? Description)
{
	/// <summary>The <see cref="SettingsRow"/>'s anchor on the page, which is what a search result scrolls to.</summary>
	public string Anchor => SettingsAnchor.Of(Label);
}
