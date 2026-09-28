using System.Net.Sockets;
using FluentFTP.Exceptions;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ftp.FileSystem;

namespace Mokaterm.Modules.Ftp.Tests;

public sealed class FtpErrorsTests
{
	[Theory]
	[InlineData("550", "No such file or directory", RemoteFileErrorKind.NotFound)]
	[InlineData("550", "The system cannot find the file specified.", RemoteFileErrorKind.NotFound)]
	[InlineData("550", "/etc/shadow: Permission denied", RemoteFileErrorKind.PermissionDenied)]
	[InlineData("550", "Access is denied.", RemoteFileErrorKind.PermissionDenied)]
	[InlineData("550", "Directory not empty", RemoteFileErrorKind.DirectoryNotEmpty)]
	[InlineData("550", "Can't create directory: File exists", RemoteFileErrorKind.AlreadyExists)]
	[InlineData("553", "Could not create file.", RemoteFileErrorKind.PermissionDenied)]
	[InlineData("553", "Rename failed: already exists", RemoteFileErrorKind.AlreadyExists)]
	[InlineData("530", "Login incorrect.", RemoteFileErrorKind.PermissionDenied)]
	[InlineData("532", "Need account for storing files.", RemoteFileErrorKind.PermissionDenied)]
	[InlineData("421", "Timeout.", RemoteFileErrorKind.ConnectionLost)]
	[InlineData("425", "Can't open data connection.", RemoteFileErrorKind.ConnectionLost)]
	[InlineData("426", "Connection closed; transfer aborted.", RemoteFileErrorKind.ConnectionLost)]
	[InlineData("500", "Unknown SITE command.", RemoteFileErrorKind.NotSupported)]
	[InlineData("502", "Command not implemented.", RemoteFileErrorKind.NotSupported)]
	[InlineData("504", "Command not implemented for that parameter.", RemoteFileErrorKind.NotSupported)]
	[InlineData("521", "\"/tmp/x\" directory already exists", RemoteFileErrorKind.AlreadyExists)]
	[InlineData("450", "File busy.", RemoteFileErrorKind.Unknown)]
	[InlineData("452", "Insufficient storage space.", RemoteFileErrorKind.Unknown)]
	[InlineData("552", "Exceeded storage allocation.", RemoteFileErrorKind.Unknown)]
	[InlineData("501", "Syntax error in parameters or arguments.", RemoteFileErrorKind.Unknown)]
	public void ClassifyReply_MapsCodeAndText(string code, string message, RemoteFileErrorKind expected) =>
		Assert.Equal(expected, FtpErrors.ClassifyReply(code, message, RemoteFileErrorKind.Unknown));

	[Theory]
	[InlineData(RemoteFileErrorKind.NotFound)]
	[InlineData(RemoteFileErrorKind.PermissionDenied)]
	public void ClassifyReply_550WithoutHint_UsesTheCommandsFallback(RemoteFileErrorKind unavailable) =>
		Assert.Equal(unavailable, FtpErrors.ClassifyReply("550", "Create directory operation failed.", unavailable));

	[Theory]
	[InlineData("530", "Login incorrect.", true)]
	[InlineData("430", "Invalid user name or password", true)]
	[InlineData("530", "Sorry, the maximum number of clients (3) from your host are already connected.", false)]
	[InlineData("421", "Too many connections (8) from this IP", false)]
	[InlineData("530", "Non-anonymous sessions must use encryption.", false)]
	[InlineData("500", "USER: command requires a parameter", false)]
	public void IsLoginRejected_OnlyForBadCredentials(string code, string message, bool expected) =>
		Assert.Equal(expected, FtpErrors.IsLoginRejected(new FtpAuthenticationException(code, message)));

	[Fact]
	public void IsLoginRejected_IgnoresCommandFailuresOutsideLogin() =>
		Assert.False(FtpErrors.IsLoginRejected(new FtpCommandException("530", "Not logged in.")));

	[Theory]
	[InlineData("421", "Service not available, closing control connection.", true)]
	[InlineData("530", "Too many users - please try again later", true)]
	[InlineData("530", "Login incorrect.", false)]
	[InlineData("550", "No such file", false)]
	public void IsConnectionLimit_RecognisesRefusedConnections(string code, string message, bool expected) =>
		Assert.Equal(expected, FtpErrors.IsConnectionLimit(new FtpCommandException(code, message)));

	[Theory]
	[InlineData("530", "Non-anonymous sessions must use encryption.", true)]
	[InlineData("534", "Policy requires SSL.", true)]
	[InlineData("530", "Login incorrect.", false)]
	public void IsTlsRequired_RecognisesEncryptionPolicies(string code, string message, bool expected) =>
		Assert.Equal(expected, FtpErrors.IsTlsRequired(new FtpCommandException(code, message)));

	[Fact]
	public void ToRemoteException_FindsReplyInsideFluentFtpWrapper()
	{
		FtpException wrapped = new("Error while downloading the file from the server. See InnerException for more info.", new FtpCommandException("550", "No such file or directory"));

		RemoteFileSystemException exception = FtpErrors.ToRemoteException(wrapped, "download", "/srv/missing.bin", RemoteFileErrorKind.Unknown);

		Assert.Equal(RemoteFileErrorKind.NotFound, exception.Kind);
		Assert.Equal("/srv/missing.bin", exception.Path);
		Assert.Equal("Could not download /srv/missing.bin: 550 No such file or directory.", exception.Message);
		Assert.Same(wrapped, exception.InnerException);
	}

	[Fact]
	public void ToRemoteException_PassesRemoteExceptionsThrough()
	{
		RemoteFileSystemException original = new(RemoteFileErrorKind.AlreadyExists, "exists", "/x");

		Assert.Same(original, FtpErrors.ToRemoteException(original, "upload", "/x", RemoteFileErrorKind.Unknown));
	}

	[Fact]
	public void ToRemoteException_TimeoutIsConnectionLost()
	{
		RemoteFileSystemException exception = FtpErrors.ToRemoteException(new TimeoutException("Timed out"), "list", "/", RemoteFileErrorKind.NotFound);

		Assert.Equal(RemoteFileErrorKind.ConnectionLost, exception.Kind);
		Assert.Contains("did not respond in time", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ToRemoteException_SocketFailureIsConnectionLost() =>
		Assert.Equal(RemoteFileErrorKind.ConnectionLost, FtpErrors.ToRemoteException(new SocketException((int)SocketError.ConnectionReset), "list", "/", RemoteFileErrorKind.NotFound).Kind);

	[Fact]
	public void ToRemoteException_RejectedLoginOnTransferConnectionIsPermissionDenied()
	{
		ProtocolConnectException refused = new(ConnectFailure.AuthenticationFailed, "The server rejected the login.");

		Assert.Equal(RemoteFileErrorKind.PermissionDenied, FtpErrors.ToRemoteException(refused, "upload", "/a", RemoteFileErrorKind.Unknown).Kind);
	}

	[Fact]
	public void ToRemoteException_UnknownFailureKeepsItsMessage()
	{
		RemoteFileSystemException exception = FtpErrors.ToRemoteException(new InvalidOperationException("Odd state"), "move", "/a", RemoteFileErrorKind.Unknown);

		Assert.Equal(RemoteFileErrorKind.Unknown, exception.Kind);
		Assert.Equal("Could not move /a: Odd state.", exception.Message);
	}

	[Fact]
	public void IsNoAnswer_IsFalseOnceTheServerReplied()
	{
		Assert.True(FtpErrors.IsNoAnswer(new IOException("reset")));
		Assert.True(FtpErrors.IsNoAnswer(new FtpException("wrapped", new SocketException((int)SocketError.ConnectionAborted))));
		Assert.True(FtpErrors.IsNoAnswer(new TimeoutException()));
		Assert.False(FtpErrors.IsNoAnswer(new FtpCommandException("550", "No such file")));
		Assert.False(FtpErrors.IsNoAnswer(new FtpException("Error while downloading", new FtpCommandException("451", "Aborted"))));
		Assert.False(FtpErrors.IsNoAnswer(new RemoteFileSystemException(RemoteFileErrorKind.NotFound, "missing")));
		Assert.False(FtpErrors.IsNoAnswer(new OperationCanceledException()));
	}

	[Fact]
	public void ReplyText_StripsTheCodePrefixFluentFtpAddsToMessage()
	{
		Assert.Equal("No such file or directory", FtpErrors.ReplyText(new FtpCommandException("550", "No such file or directory")));
		Assert.Equal("", FtpErrors.ReplyText(new FtpAuthenticationException("530", "")));
		Assert.Equal("421 Too many connections", FtpErrors.DescribeReply(new FtpCommandException("421", "Too many connections")));
	}

	[Theory]
	[InlineData("550", "No such file", "550 No such file")]
	[InlineData("550", "  ", "550")]
	[InlineData(null, "Plain", "Plain")]
	public void DescribeReply_JoinsCodeAndText(string? code, string message, string expected) =>
		Assert.Equal(expected, FtpErrors.DescribeReply(code, message));
}
