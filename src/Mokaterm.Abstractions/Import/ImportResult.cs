namespace Mokaterm.Abstractions.Import;

/// <summary>What the import created and what it left behind.</summary>
public sealed record ImportResult
{
	public int FoldersCreated { get; init; }

	public int HostsCreated { get; init; }

	/// <summary>Hosts that were already saved and took the new logins instead of being created again.</summary>
	public int HostsReused { get; init; }

	public int LoginsCreated { get; init; }

	public IReadOnlyList<ImportSkip> Skipped { get; init; } = [];

	/// <summary>Set when the import stopped early; anything already created stays.</summary>
	public string? Error { get; init; }

	public bool CreatedAnything => FoldersCreated > 0 || HostsCreated > 0 || LoginsCreated > 0;
}
