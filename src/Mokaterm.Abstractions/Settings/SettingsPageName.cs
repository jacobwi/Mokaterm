namespace Mokaterm.Abstractions.Settings;

/// <summary>
/// A settings page's id, registered as a singleton beside its descriptor (which is a UI type) so that code without the
/// UI, the plugin loader, can check what a plugin named its pages.
/// </summary>
public sealed record SettingsPageName(string Id);
