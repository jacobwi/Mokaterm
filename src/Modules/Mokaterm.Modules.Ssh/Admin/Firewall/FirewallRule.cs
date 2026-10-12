namespace Mokaterm.Modules.Ssh.Admin.Firewall;

/// <summary>One rule as the firewall lists it. For ufw the three columns; for firewalld a kind (service, port, rich rule) and its text.</summary>
internal sealed record FirewallRule(string Target, string Action, string From)
{
	/// <summary>ufw's number for the rule in <c>ufw status numbered</c>, which is how ufw deletes one; null for firewalld.</summary>
	public int? Number { get; init; }

	/// <summary>The rule's line in <c>ufw status numbered</c> after its number, which a delete checks is still there.</summary>
	public string NumberedText { get; init; } = "";

	/// <summary>A firewalld rule that is a service of the zone.</summary>
	public bool IsService => From == "service" && Action == "ALLOW";

	/// <summary>A firewalld rule that is a port of the zone.</summary>
	public bool IsPort => From == "port" && Action == "ALLOW";

	/// <summary>A firewalld rich rule, whose whole text is <see cref="Target"/>.</summary>
	public bool IsRichRule => Action == "RICH RULE";
}
