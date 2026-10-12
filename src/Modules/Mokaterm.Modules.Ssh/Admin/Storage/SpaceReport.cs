namespace Mokaterm.Modules.Ssh.Admin.Storage;

/// <summary>What du found under one folder, or what the large file search found under it.</summary>
internal sealed record SpaceReport
{
	public required string Path { get; init; }

	/// <summary>The whole folder, files directly in it included; null when du was stopped before it got there.</summary>
	public long? TotalKb { get; init; }

	/// <summary>The folders directly inside (or the large files found), largest first.</summary>
	public IReadOnlyList<SpaceEntry> Entries { get; init; } = [];

	/// <summary>How many folders or files could not be read, which root mode usually can.</summary>
	public int Unreadable { get; init; }

	/// <summary>The first thing the tool said it could not read, for a tooltip.</summary>
	public string FirstProblem { get; init; } = "";

	/// <summary>The time limit stopped the tool, so the list holds only what it finished.</summary>
	public bool StoppedEarly { get; init; }

	/// <summary>The large file search found more than it keeps; only the first ones are listed.</summary>
	public bool Capped { get; init; }

	/// <summary>The output passed the size this view keeps.</summary>
	public bool Truncated { get; init; }

	/// <summary>Space taken by files directly in the folder: the total less every folder listed.</summary>
	public long? FilesHereKb => TotalKb is { } total ? Math.Max(0, total - Entries.Sum(entry => entry.Kb)) : null;
}
