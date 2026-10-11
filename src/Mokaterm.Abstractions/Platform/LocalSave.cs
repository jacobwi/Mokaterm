namespace Mokaterm.Abstractions.Platform;

/// <summary>
/// One download's place on this machine, kept across its attempts so a retry can write where the first attempt did and
/// carry on from what it left. Created by the caller, filled in by the host; attempts run one at a time.
/// </summary>
public sealed class LocalSave
{
	public LocalSave(string suggestedName, long? length)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(suggestedName);
		SuggestedName = suggestedName;
		Length = length;
	}

	public string SuggestedName { get; }

	public long? Length { get; }

	/// <summary>Where the finished file goes. The host's to set: null until the first attempt chose it.</summary>
	public string? TargetPath { get; set; }

	/// <summary>Where the bytes go until the file is complete. The host's to set: null once nothing partial is left.</summary>
	public string? PartialPath { get; set; }

	/// <summary>The host's to set: true when the user already agreed to replace whatever is at <see cref="TargetPath"/>.</summary>
	public bool ReplaceTarget { get; set; }
}
