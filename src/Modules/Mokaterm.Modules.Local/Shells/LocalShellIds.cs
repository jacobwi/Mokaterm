using System.Diagnostics.CodeAnalysis;

namespace Mokaterm.Modules.Local.Shells;

/// <summary>The names a login stores for a shell, and the ones a host's address may use for it.</summary>
internal static class LocalShellIds
{
	public const string PowerShell = "pwsh";

	public const string WindowsPowerShell = "powershell";

	public const string CommandPrompt = "cmd";

	public const string GitBash = "git-bash";

	/// <summary>WSL's default distribution, as an address. A stored id always names the distribution.</summary>
	public const string Wsl = "wsl";

	/// <summary>The program the login names itself.</summary>
	public const string Custom = "custom";

	private const string WslPrefix = "wsl:";

	public static string ForDistribution(string distribution) => WslPrefix + distribution;

	public static bool TryGetDistribution(string id, [NotNullWhen(true)] out string? distribution)
	{
		ArgumentNullException.ThrowIfNull(id);
		string trimmed = id.Trim();
		if (trimmed.Length > WslPrefix.Length && trimmed.StartsWith(WslPrefix, StringComparison.OrdinalIgnoreCase))
		{
			distribution = trimmed[WslPrefix.Length..].Trim();
			return distribution.Length > 0;
		}

		distribution = null;
		return false;
	}

	/// <summary>What a shell this computer does not have is called in a message, from its id alone.</summary>
	public static string Describe(string id) =>
		TryGetDistribution(id, out string? distribution) ? $"the WSL distribution {distribution}"
		: id.Trim().ToLowerInvariant() switch
		{
			PowerShell => "PowerShell 7",
			WindowsPowerShell => "Windows PowerShell",
			CommandPrompt => "the Command Prompt",
			GitBash => "Git Bash",
			Wsl => "WSL",
			_ => id.Trim(),
		};
}
