namespace Mokaterm.UI.Common.Contributions;

/// <summary>
/// One settings row for the settings search. Never written by hand: the Mokaterm.Generators source generator writes one
/// for every <see cref="Components.SettingsRow"/> with a literal label in a page's markup, so the search reads the same
/// words the page shows. <see cref="SettingsSearchIndex"/> reads them back.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SettingsSearchEntryAttribute(Type page, string? section, string label, string? description) : Attribute
{
	/// <summary>The component the row is written in, which is the settings page's <see cref="SettingsPageDescriptor.Component"/>.</summary>
	public Type Page { get; } = page;

	/// <summary>The title of the <see cref="Components.SettingsSection"/> around the row, when it is literal.</summary>
	public string? Section { get; } = section;

	public string Label { get; } = label;

	/// <summary>The row's description, when it is literal.</summary>
	public string? Description { get; } = description;
}
