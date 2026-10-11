using System.Text.RegularExpressions;

namespace Mokaterm.Modules.Ssh.Admin.Packages;

/// <summary>Reads each package manager's installed list and update list.</summary>
internal static partial class PackageListParser
{
	/// <summary><c>dpkg-query</c> lines of name, version, architecture and status; only fully installed packages count.</summary>
	public static IReadOnlyList<InstalledPackage> ParseDpkg(string output)
	{
		List<InstalledPackage> packages = [];
		foreach (string line in AdminSections.Lines(output))
		{
			string[] fields = line.Split('\t');
			if (fields.Length >= 4 && fields[3].EndsWith(" installed", StringComparison.Ordinal) && fields[0].Length > 0)
			{
				packages.Add(new InstalledPackage(fields[0], fields[1], fields[2]));
			}
		}

		return Sorted(packages);
	}

	/// <summary><c>rpm -qa</c> lines of name, version-release and architecture.</summary>
	public static IReadOnlyList<InstalledPackage> ParseRpm(string output)
	{
		List<InstalledPackage> packages = [];
		foreach (string line in AdminSections.Lines(output))
		{
			string[] fields = line.Split('\t');
			if (fields.Length >= 3 && fields[0].Length > 0)
			{
				packages.Add(new InstalledPackage(fields[0], fields[1], fields[2] == "(none)" ? "" : fields[2]));
			}
		}

		return Sorted(packages);
	}

	/// <summary><c>pacman -Q</c>: name and version.</summary>
	public static IReadOnlyList<InstalledPackage> ParsePacman(string output)
	{
		List<InstalledPackage> packages = [];
		foreach (string line in AdminSections.Lines(output))
		{
			if (line.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [var name, var version])
			{
				packages.Add(new InstalledPackage(name, version, ""));
			}
		}

		return Sorted(packages);
	}

	/// <summary><c>apk info -v</c>: <c>name-version-rN</c> run together, split where the version starts.</summary>
	public static IReadOnlyList<InstalledPackage> ParseApkInstalled(string output)
	{
		List<InstalledPackage> packages = [];
		foreach (string line in AdminSections.Lines(output))
		{
			if (SplitApk(line.Trim()) is ({ } name, { } version))
			{
				packages.Add(new InstalledPackage(name, version, ""));
			}
		}

		return Sorted(packages);
	}

	/// <summary><c>apt list --upgradable</c>: <c>name/suite version arch [upgradable from: old]</c>, after a "Listing..." line.</summary>
	public static IReadOnlyList<PackageUpdate> ParseAptUpgradable(string output)
	{
		List<PackageUpdate> updates = [];
		foreach (string line in AdminSections.Lines(output))
		{
			Match match = AptUpgradable().Match(line);
			if (match.Success)
			{
				updates.Add(new PackageUpdate
				{
					Name = match.Groups["name"].Value,
					Source = match.Groups["suite"].Value,
					NewVersion = match.Groups["version"].Value,
					Architecture = match.Groups["arch"].Value,
					CurrentVersion = match.Groups["old"].Value,
				});
			}
		}

		return SortedUpdates(updates);
	}

	/// <summary>
	/// <c>dnf</c> and <c>yum check-update</c>: <c>name.arch version repo</c> columns. Everything after "Obsoleting
	/// Packages" lists replacements, not updates, and metadata or security notices have another shape.
	/// </summary>
	public static IReadOnlyList<PackageUpdate> ParseCheckUpdate(string output)
	{
		List<PackageUpdate> updates = [];
		foreach (string line in AdminSections.Lines(output))
		{
			if (line.StartsWith("Obsoleting", StringComparison.Ordinal))
			{
				break;
			}

			string[] fields = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if (fields.Length != 3 || line[0] == ' ')
			{
				continue;
			}

			int dot = fields[0].LastIndexOf('.');
			if (dot <= 0 || !char.IsAsciiDigit(fields[1][0]) && !fields[1].Contains(':', StringComparison.Ordinal))
			{
				continue;
			}

			updates.Add(new PackageUpdate
			{
				Name = fields[0][..dot],
				Architecture = fields[0][(dot + 1)..],
				NewVersion = fields[1],
				Source = fields[2],
			});
		}

		return SortedUpdates(updates);
	}

	/// <summary><c>zypper list-updates</c>: a table of <c>S | Repository | Name | Current Version | Available Version | Arch</c>.</summary>
	public static IReadOnlyList<PackageUpdate> ParseZypperUpdates(string output)
	{
		List<PackageUpdate> updates = [];
		foreach (string line in AdminSections.Lines(output))
		{
			string[] cells = line.Split('|');
			if (cells.Length < 6 || cells[2].Trim() is "Name" or "" || cells[0].Trim().StartsWith('-'))
			{
				continue;
			}

			updates.Add(new PackageUpdate
			{
				Source = cells[1].Trim(),
				Name = cells[2].Trim(),
				CurrentVersion = cells[3].Trim(),
				NewVersion = cells[4].Trim(),
				Architecture = cells[5].Trim(),
			});
		}

		return SortedUpdates(updates);
	}

	/// <summary><c>checkupdates</c> and <c>pacman -Qu</c>: <c>name old -> new</c>, sometimes with <c>[ignored]</c>.</summary>
	public static IReadOnlyList<PackageUpdate> ParsePacmanUpdates(string output)
	{
		List<PackageUpdate> updates = [];
		foreach (string line in AdminSections.Lines(output))
		{
			string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (fields.Length >= 4 && fields[2] == "->")
			{
				updates.Add(new PackageUpdate { Name = fields[0], CurrentVersion = fields[1], NewVersion = fields[3] });
			}
		}

		return SortedUpdates(updates);
	}

	/// <summary><c>apk version -l '&lt;'</c>: <c>name-old &lt; new</c> after an "Installed: Available:" header.</summary>
	public static IReadOnlyList<PackageUpdate> ParseApkVersion(string output)
	{
		List<PackageUpdate> updates = [];
		foreach (string line in AdminSections.Lines(output))
		{
			string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (fields.Length >= 3 && fields[1] == "<" && SplitApk(fields[0]) is ({ } name, { } current))
			{
				updates.Add(new PackageUpdate { Name = name, CurrentVersion = current, NewVersion = fields[2] });
			}
		}

		return SortedUpdates(updates);
	}

	/// <summary>
	/// apk joins name and version with a dash, and names have dashes too (<c>py3-foo-2.0-r1</c>). The version is the
	/// shortest tail that starts with a digit and ends in the package release, <c>-rN</c>.
	/// </summary>
	internal static (string? Name, string? Version) SplitApk(string text)
	{
		Match match = ApkNameVersion().Match(text);
		return match.Success ? (match.Groups["name"].Value, match.Groups["version"].Value) : (null, null);
	}

	private static List<InstalledPackage> Sorted(List<InstalledPackage> packages)
	{
		packages.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
		return packages;
	}

	private static List<PackageUpdate> SortedUpdates(List<PackageUpdate> updates)
	{
		updates.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
		return updates;
	}

	[GeneratedRegex(@"^(?<name>[^/\s]+)/(?<suite>\S+)\s+(?<version>\S+)\s+(?<arch>\S+)(?:\s+\[upgradable from:\s*(?<old>[^\]]+)\])?", RegexOptions.CultureInvariant)]
	private static partial Regex AptUpgradable();

	[GeneratedRegex(@"^(?<name>.+?)-(?<version>[0-9][^-\s]*-r[0-9]+)$", RegexOptions.CultureInvariant)]
	private static partial Regex ApkNameVersion();
}
