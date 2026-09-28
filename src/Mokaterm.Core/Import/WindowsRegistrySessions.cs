using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Mokaterm.Core.Import;

/// <summary>
/// Reads session keys under HKEY_CURRENT_USER. Every key is opened read only and nothing here writes: the import
/// must never change another client's settings.
/// </summary>
internal static class WindowsRegistrySessions
{
	/// <summary>A session key with far more values than this is not one; the guard keeps a damaged hive cheap.</summary>
	private const int MaxValuesPerKey = 512;

	public static bool IsSupported => OperatingSystem.IsWindows();

	/// <summary>
	/// Returns the direct subkeys of <paramref name="path"/> with their values. An empty list means the key is not
	/// there, which is how "that client is not installed" reaches the wizard.
	/// </summary>
	public static IReadOnlyList<ImportSection> ReadSubKeys(string path)
	{
		return OperatingSystem.IsWindows() ? Read(path) : [];
	}

	[SupportedOSPlatform("windows")]
	private static List<ImportSection> Read(string path)
	{
		try
		{
			using RegistryKey? root = Registry.CurrentUser.OpenSubKey(path, writable: false);
			if (root is null)
			{
				return [];
			}

			List<ImportSection> sections = [];
			foreach (string name in root.GetSubKeyNames())
			{
				using RegistryKey? key = root.OpenSubKey(name, writable: false);
				if (key is null)
				{
					continue;
				}

				Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
				foreach (string valueName in key.GetValueNames().Take(MaxValuesPerKey))
				{
					if (Format(key.GetValue(valueName)) is { } value)
					{
						values[valueName] = value;
					}
				}

				if (values.Count > 0)
				{
					sections.Add(new ImportSection(path + "\\" + name, values));
				}
			}

			return sections;
		}
		catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
		{
			return [];
		}
	}

	private static string? Format(object? value) => value switch
	{
		string text => text,
		int number => number.ToString(CultureInfo.InvariantCulture),
		long number => number.ToString(CultureInfo.InvariantCulture),

		// Binary and multi-string values carry nothing a session needs; passwords are deliberately among them.
		_ => null,
	};
}
