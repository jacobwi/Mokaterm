namespace Mokaterm.Core.Import;

/// <summary>A key path turned into a full path, or the sentence saying why it could not be.</summary>
/// <param name="WasRelative">The path was relative, so it was looked for under <c>~/.ssh</c>.</param>
internal readonly record struct IdentityPathResult(string? FullPath, string? Reason, bool WasRelative)
{
	public static IdentityPathResult Problem(string reason) => new(null, reason, false);
}
