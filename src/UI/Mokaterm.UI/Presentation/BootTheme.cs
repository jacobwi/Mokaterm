using Mokaterm.Abstractions.Settings;

namespace Mokaterm.UI.Presentation;

/// <summary>
/// The theme mode a web host writes on its page's <c>html</c> element as <c>data-mt-mode</c>, so the splash before the
/// circuit starts is drawn in the user's mode (mokaterm.css) rather than the device's, which is all moka-boot.css knows.
/// </summary>
public static class BootTheme
{
	/// <summary>"light" or "dark". Loads the settings first; a file that cannot be read is the default, dark.</summary>
	public static async Task<string> ModeAsync(ISettingsService settings, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(settings);
		try
		{
			await settings.LoadAsync(cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The circuit loads them again and reports the failure where the user can see it.
		}

		return settings.Get<AppearanceSettings>().Mode == ThemeMode.Light ? "light" : "dark";
	}
}
