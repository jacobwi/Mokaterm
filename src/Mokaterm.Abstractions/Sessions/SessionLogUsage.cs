namespace Mokaterm.Abstractions.Sessions;

/// <summary>How many session logs the folder holds and what they take together, the ones past the listing's limit included.</summary>
public sealed record SessionLogUsage(int Files, long Bytes)
{
	public static readonly SessionLogUsage Empty = new(0, 0);
}
