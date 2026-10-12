using Moka.Red.Core.Icons;

namespace Mokaterm.Modules.Ssh.Components;

/// <summary>One page in a tool's navigation, and why it cannot be used here, if it cannot.</summary>
public sealed record ToolNavEntry<TPage>(TPage Page, string Label, MokaIconDefinition Icon, string? Unavailable = null);
