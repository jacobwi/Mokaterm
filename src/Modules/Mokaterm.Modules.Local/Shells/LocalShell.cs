namespace Mokaterm.Modules.Local.Shells;

/// <summary>A shell this computer can run, found by <see cref="LocalShellDetector"/> or named by a login.</summary>
internal sealed record LocalShell
{
	/// <summary>What a login stores to pick it, such as <c>pwsh</c> or <c>wsl:Ubuntu</c>.</summary>
	public required string Id { get; init; }

	public required LocalShellKind Kind { get; init; }

	/// <summary>What pickers and progress messages call it, such as <c>PowerShell</c> or <c>Ubuntu (WSL)</c>.</summary>
	public required string DisplayName { get; init; }

	/// <summary>The full path of the program that runs.</summary>
	public required string Program { get; init; }

	/// <summary>The distribution a <see cref="LocalShellKind.Wsl"/> shell opens.</summary>
	public string? Distribution { get; init; }

	/// <summary>What a host's address says to name this shell: the distribution's own name for WSL, the id otherwise.</summary>
	public string Address => Distribution ?? Id;
}
