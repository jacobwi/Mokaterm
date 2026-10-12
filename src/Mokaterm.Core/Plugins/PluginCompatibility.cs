using System.Globalization;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Core.Plugins;

/// <summary>
/// Which plugins this build can run. While Mokaterm is on 0.x every minor version may change the surface plugins build
/// against, so a plugin built for 0.2 runs on any 0.2.x and nothing else; from 1.0 the major version decides.
/// </summary>
public static class PluginCompatibility
{
	/// <summary>This build's plugin version, from the contracts assembly plugins compile against.</summary>
	public static Version HostSdk { get; } = ReadHostSdk();

	/// <summary>Why a plugin cannot run here, in a sentence for the user, or null when it can.</summary>
	internal static string? Problem(PluginManifest manifest, string hostName)
	{
		if (manifest.Hosts.Count > 0 && !manifest.Hosts.Contains(hostName))
		{
			return hostName == PluginHosts.Web ? "This plugin is for the desktop app only." : "This plugin is for the web app only.";
		}

		// Data needs no particular version: a theme reads the same in every build.
		if (manifest.HasCode && TryParseSdk(manifest.Sdk, out Version? sdk) && !Matches(sdk))
		{
			return $"This plugin was built for Mokaterm {Describe(sdk)}, and this is Mokaterm {Describe(HostSdk)}. It needs a version built for this one.";
		}

		return null;
	}

	internal static bool TryParseSdk(string? text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Version? version)
	{
		version = null;
		string[] parts = (text ?? "").Split('.');
		if (parts.Length != 2
			|| !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
			|| !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
		{
			return false;
		}

		version = new Version(major, minor);
		return true;
	}

	private static bool Matches(Version sdk) =>
		HostSdk.Major == 0 ? sdk.Major == 0 && sdk.Minor == HostSdk.Minor : sdk.Major == HostSdk.Major;

	private static string Describe(Version version) => version.Major == 0 ? $"{version.Major}.{version.Minor}" : $"{version.Major}.x";

	private static Version ReadHostSdk()
	{
		Version version = typeof(IMokatermModule).Assembly.GetName().Version ?? new Version(0, 0);
		return new Version(version.Major, version.Minor);
	}
}
