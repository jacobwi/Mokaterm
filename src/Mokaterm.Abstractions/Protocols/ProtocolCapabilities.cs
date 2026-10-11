namespace Mokaterm.Abstractions.Protocols;

/// <summary>What a protocol's sessions can do. The shell picks views from these before a session exists.</summary>
[Flags]
public enum ProtocolCapabilities
{
	None = 0,

	/// <summary>Sessions expose <see cref="Terminal.ITerminalChannel"/>.</summary>
	Terminal = 1 << 0,

	/// <summary>Sessions expose <see cref="FileSystem.IFileSystemFeature"/>.</summary>
	FileSystem = 1 << 1,

	/// <summary>The file system can run operations as root, for example through sudo.</summary>
	Elevation = 1 << 2,

	/// <summary>Reserved for RDP and VNC modules.</summary>
	RemoteDesktop = 1 << 3,

	/// <summary>Reserved for message broker modules such as MQTT.</summary>
	Messaging = 1 << 4,

	/// <summary>
	/// Sessions expose <see cref="Commands.IRemoteCommandFeature"/>: a command runs to completion without a terminal,
	/// so the provider also honours <see cref="ConnectPurpose.Commands"/>. A protocol whose only way to run anything is
	/// typing into a login (telnet, a serial console) never declares it.
	/// </summary>
	Commands = 1 << 5,
}
