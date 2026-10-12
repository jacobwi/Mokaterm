namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One image's update check: the reference asked about, what was found, and a sentence that says it.</summary>
/// <param name="Reference">The reference as it was asked about, for example <c>nginx:1.27-alpine</c>.</param>
/// <param name="Detail">What a page shows on hover, written for the user.</param>
internal sealed record ImageUpdateResult(string Reference, ImageUpdateState State, string Detail, DateTimeOffset CheckedAt)
{
	/// <summary>The digest the registry has for the tag now, when the check got that far.</summary>
	public string? RemoteDigest { get; init; }

	public bool HasUpdate => State == ImageUpdateState.UpdateAvailable;

	/// <summary>True when the registry answered, either way.</summary>
	public bool IsKnown => State is ImageUpdateState.UpToDate or ImageUpdateState.UpdateAvailable;

	/// <summary>The micro-label a row shows for it.</summary>
	public string Label => State switch
	{
		ImageUpdateState.UpdateAvailable => "UPDATE",
		ImageUpdateState.UpToDate => "CURRENT",
		ImageUpdateState.LocalOnly => "LOCAL",
		ImageUpdateState.NoTag => "",
		_ => "UNCHECKED",
	};
}
