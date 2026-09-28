using Mokaterm.Abstractions.Connections;

namespace Mokaterm.Abstractions.Protocols;

/// <summary>Static facts about a protocol, shown in editors and used to route sessions to views.</summary>
public sealed record ProtocolDescriptor
{
	/// <summary>Stable lowercase id stored in saved connections, for example <c>ssh</c>, <c>sftp</c> or <c>ftp</c>.</summary>
	public required string Id { get; init; }

	public required string DisplayName { get; init; }

	public string Description { get; init; } = "";

	public required int DefaultPort { get; init; }

	/// <summary>
	/// False for a protocol that has no network port at all: a serial line's machine is a port name (COM3,
	/// /dev/ttyUSB0), so there is nothing a number could mean. The connection editor then shows no port field and
	/// labels leave the number out, and <see cref="DefaultPort"/> is never used.
	/// </summary>
	public bool UsesPort { get; init; } = true;

	public required ProtocolCapabilities Capabilities { get; init; }

	/// <summary>Methods the connection editor offers. The first one is the default.</summary>
	public IReadOnlyList<AuthenticationMethod> AuthenticationMethods { get; init; } = [AuthenticationMethod.Password];

	public bool RequiresUsername { get; init; } = true;

	/// <summary>
	/// Id of the protocol this one is a variant of. <c>sftp</c> is a variant of <c>ssh</c>: it shares the
	/// connection options editor and can be opened from any SSH connection.
	/// </summary>
	public string? VariantOf { get; init; }

	/// <summary>Sort key for protocol pickers.</summary>
	public int Order { get; init; }

	public bool Has(ProtocolCapabilities capabilities) => (Capabilities & capabilities) == capabilities;
}
