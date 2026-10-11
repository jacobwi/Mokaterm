namespace Mokaterm.Modules.Ssh.Admin.Network;

/// <summary>One route. <see cref="Destination"/> is <c>default</c> for the default route.</summary>
internal sealed record NetRoute
{
	public required string Destination { get; init; }

	public string Gateway { get; init; } = "";

	public string Device { get; init; } = "";

	public string Protocol { get; init; } = "";

	public string Source { get; init; } = "";

	public int? Metric { get; init; }

	public bool IsIpv6 { get; init; }

	public bool IsDefault => Destination == "default";
}
