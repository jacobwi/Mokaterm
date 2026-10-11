namespace Mokaterm.Modules.Ssh.Admin.Firewall;

/// <summary>One rule as the firewall lists it. For ufw the three columns; for firewalld a kind (service, port, rich rule) and its text.</summary>
internal sealed record FirewallRule(string Target, string Action, string From);
