namespace Mokaterm.Abstractions.Connections;

/// <summary>Tags a host so the shell can warn before risky actions and tint production sessions.</summary>
public enum HostEnvironment
{
	None,
	Development,
	Staging,
	Production,
}
