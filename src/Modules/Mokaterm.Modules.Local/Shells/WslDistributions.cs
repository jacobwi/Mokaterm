namespace Mokaterm.Modules.Local.Shells;

/// <summary>Turns what the registry lists into the distributions worth offering.</summary>
internal static class WslDistributions
{
	/// <summary>
	/// Docker Desktop installs distributions of its own (<c>docker-desktop</c>, <c>docker-desktop-data</c>) to run its
	/// engine in. Nobody opens a shell in them, and listing them only crowds the picker.
	/// </summary>
	private const string DockerPrefix = "docker-desktop";

	/// <summary>
	/// Every named distribution once, Docker's own left out, the default one first and the rest by name.
	/// </summary>
	public static IReadOnlyList<WslDistribution> Select(IEnumerable<(string? Name, bool IsDefault)> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		List<WslDistribution> distributions = [];
		foreach ((string? name, bool isDefault) in entries)
		{
			string trimmed = name?.Trim() ?? "";
			if (trimmed.Length == 0 || trimmed.StartsWith(DockerPrefix, StringComparison.OrdinalIgnoreCase) || !seen.Add(trimmed))
			{
				continue;
			}

			distributions.Add(new WslDistribution(trimmed, isDefault));
		}

		return
		[
			.. distributions
				.OrderByDescending(distribution => distribution.IsDefault)
				.ThenBy(distribution => distribution.Name, StringComparer.OrdinalIgnoreCase),
		];
	}
}
