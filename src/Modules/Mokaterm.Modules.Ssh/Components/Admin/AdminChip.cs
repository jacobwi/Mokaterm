namespace Mokaterm.Modules.Ssh.Components.Admin;

/// <summary>One filter chip: what it selects, what it says, how many it would show, and whether that count is trouble.</summary>
public sealed record AdminChip(string Value, string Label, int? Count = null, bool Alert = false);
