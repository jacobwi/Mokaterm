namespace Mokaterm.Abstractions.Settings;

/// <summary>
/// The key combinations the user chose, by command id. A command that is not in here keeps the shell's default, and an
/// entry with an empty code turns its shortcut off.
/// </summary>
public sealed record ShortcutSettings : ISettingsSection
{
	public static string SectionKey => "shortcuts";

	public IReadOnlyDictionary<string, ShortcutGesture> Bindings { get; init; } =
		new Dictionary<string, ShortcutGesture>(StringComparer.Ordinal);
}
