namespace Mokaterm.Modules.Ssh.Admin.Packages;

/// <summary>One package with a newer version available. Each manager says a different part of this, so most is optional.</summary>
internal sealed record PackageUpdate
{
	public required string Name { get; init; }

	public required string NewVersion { get; init; }

	public string CurrentVersion { get; init; } = "";

	public string Architecture { get; init; } = "";

	/// <summary>The repository or suite it comes from.</summary>
	public string Source { get; init; } = "";
}
