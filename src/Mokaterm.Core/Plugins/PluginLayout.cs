namespace Mokaterm.Core.Plugins;

/// <summary>The names inside a plugin package and inside the plugins folder.</summary>
internal static class PluginLayout
{
	public const string ManifestFile = "plugin.json";

	public const string LibFolder = "lib";

	public const string WebRootFolder = "wwwroot";

	public const string ThemesFolder = "themes";

	/// <summary>Installs that wait for the next start, one folder per plugin id.</summary>
	public const string PendingFolder = ".pending";

	/// <summary>Which plugins are on and which files the user trusted. Nothing in it is secret or names a machine.</summary>
	public const string StateFile = "plugins.json";

	/// <summary>Written before plugins load and removed once the app is up; still there means the last start died.</summary>
	public const string StartMarker = ".starting";

	/// <summary>The only names a package may hold at its top level.</summary>
	public static readonly string[] TopLevel = [ManifestFile, LibFolder, WebRootFolder, ThemesFolder];

	/// <summary>Plugin folders are named by id; anything starting with a dot is ours (pending installs, the marker).</summary>
	public static bool IsPluginFolder(string name) => name.Length > 0 && name[0] != '.';
}
