namespace Mokaterm.Modules.Ssh.Components.Admin;

internal sealed record DnsRow(string Scope, IReadOnlyList<string> Values, bool IsSearch = false);
