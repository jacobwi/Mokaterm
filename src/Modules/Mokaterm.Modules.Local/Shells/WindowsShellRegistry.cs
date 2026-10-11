using System.Security;
using Microsoft.Win32;

namespace Mokaterm.Modules.Local.Shells;

/// <summary>
/// What the registry says about shells: the WSL distributions this user has, and where Git for Windows went. Reads
/// only, and nothing at all off Windows, so <c>Microsoft.Win32.Registry</c> never runs where it cannot.
/// </summary>
internal static class WindowsShellRegistry
{
	/// <summary>
	/// One subkey per distribution, each with its <c>DistributionName</c>, and the default's id beside them. Reading it
	/// beats running <c>wsl.exe -l</c>: no process to wait for, and no chance of starting the WSL service just to look.
	/// </summary>
	private const string LxssKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

	private const string GitForWindowsKey = @"SOFTWARE\GitForWindows";

	public static IReadOnlyList<WslDistribution> ReadWslDistributions()
	{
		if (!OperatingSystem.IsWindows())
		{
			return [];
		}

		try
		{
			using RegistryKey? lxss = Registry.CurrentUser.OpenSubKey(LxssKey, writable: false);
			if (lxss is null)
			{
				return [];
			}

			string? defaultId = lxss.GetValue("DefaultDistribution") as string;
			List<(string? Name, bool IsDefault)> entries = [];
			foreach (string id in lxss.GetSubKeyNames())
			{
				using RegistryKey? distribution = lxss.OpenSubKey(id, writable: false);
				entries.Add((distribution?.GetValue("DistributionName") as string, string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)));
			}

			return WslDistributions.Select(entries);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
		{
			return [];
		}
	}

	/// <summary>Where the Git for Windows installer says it put Git, machine-wide first and then for this user.</summary>
	public static IReadOnlyList<string> ReadGitInstallFolders()
	{
		if (!OperatingSystem.IsWindows())
		{
			return [];
		}

		List<string> folders = [];
		foreach (RegistryKey hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
		{
			try
			{
				using RegistryKey? git = hive.OpenSubKey(GitForWindowsKey, writable: false);
				if (git?.GetValue("InstallPath") is string { Length: > 0 } path)
				{
					folders.Add(path);
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
			{
				// Unreadable is the same as not installed there; the usual folders are still looked at.
			}
		}

		return folders;
	}
}
