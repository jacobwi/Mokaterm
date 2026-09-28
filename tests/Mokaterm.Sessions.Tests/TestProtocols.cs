using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Sessions.Tests;

internal static class TestProtocols
{
	public static ProtocolDescriptor Ssh { get; } = new()
	{
		Id = "ssh",
		DisplayName = "SSH",
		DefaultPort = 22,
		Capabilities = ProtocolCapabilities.Terminal | ProtocolCapabilities.FileSystem,
		AuthenticationMethods = [AuthenticationMethod.Password, AuthenticationMethod.PublicKey],
		Order = 0,
	};

	public static ProtocolDescriptor Sftp { get; } = new()
	{
		Id = "sftp",
		DisplayName = "SFTP",
		DefaultPort = 22,
		Capabilities = ProtocolCapabilities.FileSystem,
		VariantOf = "ssh",
		Order = 1,
	};

	public static ProtocolDescriptor Ftp { get; } = new()
	{
		Id = "ftp",
		DisplayName = "FTP",
		DefaultPort = 21,
		Capabilities = ProtocolCapabilities.FileSystem,
		AuthenticationMethods = [AuthenticationMethod.Password, AuthenticationMethod.Anonymous],
		Order = 2,
	};
}
