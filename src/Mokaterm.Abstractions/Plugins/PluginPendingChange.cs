namespace Mokaterm.Abstractions.Plugins;

/// <summary>A change to a plugin that takes effect at the next start, since a loaded assembly cannot be unloaded.</summary>
public enum PluginPendingChange
{
	None,
	Install,
	Update,
	Remove,
	Enable,
	Disable,
}
