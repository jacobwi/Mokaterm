namespace Mokaterm.Core.Import;

/// <summary>
/// One <c>Host</c> block. Settings keep their file order because OpenSSH takes the first value it sees for a
/// keyword, not the last.
/// </summary>
internal sealed record OpenSshConfigBlock(IReadOnlyList<string> Patterns, IReadOnlyList<KeyValuePair<string, string>> Settings);
