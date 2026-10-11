namespace Mokaterm.Abstractions.Protocols;

/// <summary>What a connection is opened for, which decides what a provider sets up beyond the login.</summary>
public enum ConnectPurpose
{
	/// <summary>A tab: the terminal, the saved tunnels, the startup command and whatever else the protocol shows.</summary>
	Interactive,

	/// <summary>
	/// Running commands with nothing on screen. A provider that declares <see cref="ProtocolCapabilities.Commands"/>
	/// opens no shell, starts no tunnel and types no startup command, so a run across forty machines leaves nothing
	/// behind on any of them but the commands themselves.
	/// </summary>
	Commands,
}
