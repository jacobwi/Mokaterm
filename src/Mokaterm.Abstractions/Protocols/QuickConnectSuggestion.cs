namespace Mokaterm.Abstractions.Protocols;

/// <summary>
/// One address a protocol offers to quick connect: what goes in the box (<c>COM3</c>, <c>Ubuntu</c>) and a few words
/// on what it is (<c>USB serial port</c>, <c>WSL distribution</c>).
/// </summary>
public sealed record QuickConnectSuggestion(string Address, string? Description = null);
