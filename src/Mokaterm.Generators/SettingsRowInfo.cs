namespace Mokaterm.Generators;

/// <summary>One <c>SettingsRow</c> as its markup writes it, with the title of the <c>SettingsSection</c> around it.</summary>
internal sealed record SettingsRowInfo(string? Section, string Label, string? Description);
