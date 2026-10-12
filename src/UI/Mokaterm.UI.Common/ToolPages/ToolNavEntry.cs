using Moka.Red.Core.Icons;

namespace Mokaterm.UI.Common.ToolPages;

/// <summary>One page in a tool's navigation, by its id, and why it cannot be used here, if it cannot.</summary>
public sealed record ToolNavEntry(string Id, string Label, MokaIconDefinition Icon, string? Unavailable = null);
