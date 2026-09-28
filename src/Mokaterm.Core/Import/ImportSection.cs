namespace Mokaterm.Core.Import;

/// <summary>
/// A named bag of values as it sits in a registry key, a <c>.reg</c> export or an ini section. The three readers
/// produce the same shape so one mapper can turn any of them into an entry.
/// </summary>
/// <param name="Path">Full path with backslashes, still encoded the way the client wrote it.</param>
internal sealed record ImportSection(string Path, IReadOnlyDictionary<string, string> Values);
