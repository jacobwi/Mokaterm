using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.UI.Shell;

/// <summary>Opens a settings page through the window's shell state.</summary>
internal sealed class ShellSettingsNavigator(ShellState shell) : ISettingsNavigator
{
	public void Open(string pageId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
		shell.OpenSettings(pageId);
	}
}
