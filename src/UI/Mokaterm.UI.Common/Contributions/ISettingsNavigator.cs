namespace Mokaterm.UI.Common.Contributions;

/// <summary>
/// Opens a settings page by its <see cref="SettingsPageDescriptor.Id"/>, from anywhere, settings pages included. The
/// shell implements it; a library that cannot reach the shell (the Plugins page linking to a plugin's own settings)
/// goes through this.
/// </summary>
public interface ISettingsNavigator
{
	void Open(string pageId);
}
