namespace Mokaterm.Modules.Ssh.Admin.Network;

/// <summary>A port something listens on. The process is only known for the login's own, unless the page reads as root.</summary>
internal sealed record ListeningSocket
{
	/// <summary><c>tcp</c>, <c>tcp6</c>, <c>udp</c>, <c>udp6</c>.</summary>
	public required string Protocol { get; init; }

	/// <summary>The address it is bound to: <c>0.0.0.0</c>, <c>::</c> or <c>*</c> for every address.</summary>
	public required string Address { get; init; }

	public required int Port { get; init; }

	public string Process { get; init; } = "";

	public int? Pid { get; init; }

	public bool IsUdp => Protocol.StartsWith("udp", StringComparison.Ordinal);
}
