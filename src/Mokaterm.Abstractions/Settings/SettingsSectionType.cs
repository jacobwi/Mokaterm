namespace Mokaterm.Abstractions.Settings;

/// <summary>
/// A settings section the app knows the type of, registered as a singleton service. The settings backup reads a section
/// through it: it checks an imported section against its type and finds the values marked
/// <see cref="MachineSpecificAttribute"/>. Every settings page built on the shared section base registers its own, so a
/// module section is known without a line of its own; a section with no page registers <see cref="Of{TSection}"/>
/// itself. The same section registered twice is still one section.
/// </summary>
public sealed record SettingsSectionType(string Key, Type Type)
{
	public static SettingsSectionType Of<TSection>() where TSection : class, ISettingsSection =>
		new(TSection.SectionKey, typeof(TSection));
}
