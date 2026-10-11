using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.UI.Shell;

/// <summary>One line in the settings search results: a whole page when <see cref="Row"/> is null, otherwise a row on it.</summary>
internal sealed record SettingsSearchResult(SettingsPageDescriptor Page, SettingsSearchEntry? Row)
{
	/// <summary>Where opening the result scrolls to, or null for the top of the page.</summary>
	public string? Anchor => Row?.Anchor;
}
