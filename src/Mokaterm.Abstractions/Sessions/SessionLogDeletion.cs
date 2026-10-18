namespace Mokaterm.Abstractions.Sessions;

/// <summary>What deleting session logs did, for the notice the settings page shows afterwards.</summary>
public sealed record SessionLogDeletion
{
	public static readonly SessionLogDeletion None = new();

	public int Deleted { get; init; }

	/// <summary>Left alone because a recording is still writing them.</summary>
	public int InUse { get; init; }

	/// <summary>Could not be deleted: no permission, or still held open by another copy of the app.</summary>
	public int Failed { get; init; }
}
