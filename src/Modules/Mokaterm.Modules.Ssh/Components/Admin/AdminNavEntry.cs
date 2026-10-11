using Moka.Red.Core.Icons;

namespace Mokaterm.Modules.Ssh.Components.Admin;

/// <summary>One page in the navigation, and why the detected system cannot use it, if it cannot.</summary>
internal sealed record AdminNavEntry(AdminPage Page, string Label, MokaIconDefinition Icon, string? Unavailable);
