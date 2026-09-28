namespace Mokaterm.Abstractions.Import;

/// <summary>Something the import left behind, with the reason shown next to it.</summary>
/// <param name="Name">The session or host as the other client named it.</param>
/// <param name="Reason">A finished sentence for the user, such as "Rlogin is not supported.".</param>
public sealed record ImportSkip(string Name, string Reason);
