using Moka.Red.Core.Icons;

namespace Mokaterm.UI.Common.Contributions;

public enum SettingsPageGroup
{
	Application,
	Security,
	Protocols,
	About,
}

/// <summary>
/// A page in Settings. The shell registers its own pages this way too, so the settings screen is built
/// entirely from descriptors.
/// </summary>
public sealed record SettingsPageDescriptor
{
	/// <summary>Stable id used in navigation, for example <c>terminal</c> or <c>ssh</c>.</summary>
	public required string Id { get; init; }

	public required string Title { get; init; }

	public string? Description { get; init; }

	public required MokaIconDefinition Icon { get; init; }

	/// <summary>A component with no required parameters, usually deriving from <see cref="Components.SettingsSectionBase{TSettings}"/>.</summary>
	public required Type Component { get; init; }

	public SettingsPageGroup Group { get; init; } = SettingsPageGroup.Protocols;

	public int Order { get; init; }

	/// <summary>Extra words the settings search matches, for example "font cursor scrollback".</summary>
	public string? Keywords { get; init; }
}
