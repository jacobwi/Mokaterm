using Mokaterm.Abstractions.Platform;

namespace Mokaterm.Abstractions.Plugins;

/// <summary>The host names a plugin's manifest lists in <c>hosts</c>.</summary>
public static class PluginHosts
{
	public const string Desktop = "desktop";

	public const string Web = "web";

	public static string For(HostKind kind) => kind == HostKind.Web ? Web : Desktop;
}
