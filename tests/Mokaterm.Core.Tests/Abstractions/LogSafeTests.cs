using System.Net.Sockets;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class LogSafeTests
{
	[Fact]
	public void Describe_ARemoteFileError_KeepsTheKindButNotThePath()
	{
		RemoteFileSystemException error = new(
			RemoteFileErrorKind.PermissionDenied,
			"Could not upload /home/alice/payroll.xlsx: permission denied.",
			"/home/alice/payroll.xlsx",
			new UnauthorizedAccessException("/home/alice/payroll.xlsx"));

		string text = LogSafe.Describe(error);

		Assert.Equal("RemoteFileSystemException (PermissionDenied) from UnauthorizedAccessException", text);
		Assert.DoesNotContain("alice", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Describe_AConnectFailure_KeepsTheFailureButNotTheHost()
	{
		ProtocolConnectException error = new(
			ConnectFailure.HostUnreachable,
			"Could not connect to db-01.corp.example:22.",
			new SocketException((int)SocketError.ConnectionRefused));

		Assert.Equal("ProtocolConnectException (HostUnreachable) from SocketException (ConnectionRefused)", LogSafe.Describe(error));
	}

	[Fact]
	public void Describe_AnythingElse_IsItsTypeName() =>
		Assert.Equal("TimeoutException", LogSafe.Describe(new TimeoutException("db-01.corp.example did not answer")));
}
