using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Elevation;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class SudoErrorsTests
{
	[Theory]
	[InlineData("Sorry, try again.\nsudo: no password was provided\nsudo: 1 incorrect password attempt\n", 1, nameof(SudoFailure.IncorrectPassword))]
	[InlineData("sudo: 3 incorrect password attempts\n", 1, nameof(SudoFailure.IncorrectPassword))]
	[InlineData("sudo: pam_authenticate: Authentication failure\n", 1, nameof(SudoFailure.IncorrectPassword))]
	[InlineData("sudo: a password is required\n", 1, nameof(SudoFailure.PasswordRequired))]
	[InlineData("abc is not in the sudoers file.  This incident will be reported.\n", 1, nameof(SudoFailure.NotAllowed))]
	[InlineData("Sorry, user abc is not allowed to execute '/bin/sh -c true' as root on web01.\n", 1, nameof(SudoFailure.NotAllowed))]
	[InlineData("Sorry, user abc may not run sudo on web01.\n", 1, nameof(SudoFailure.NotAllowed))]
	[InlineData("sudo: sorry, you must have a tty to run sudo\n", 1, nameof(SudoFailure.TerminalRequired))]
	[InlineData("sudo: a terminal is required to read the password; either use the -S option to read from standard input or configure an askpass helper\n", 1, nameof(SudoFailure.TerminalRequired))]
	[InlineData("sh: 1: sudo: not found\n", 127, nameof(SudoFailure.NotInstalled))]
	[InlineData("bash: line 1: doas: command not found\n", 127, nameof(SudoFailure.NotInstalled))]
	[InlineData("This service allows sftp connections only.\n", 1, nameof(SudoFailure.Other))]
	public void Classify_RecognisesSudoMessages(string error, int exitStatus, string expected) =>
		Assert.Equal(Enum.Parse<SudoFailure>(expected), SudoErrors.Classify(error, exitStatus));

	[Fact]
	public void Split_SeparatesSudoOutputFromScriptErrors()
	{
		string error = "sudo: unable to resolve host web01\nMOKATERM-ELEVATED\nrm: cannot remove '/x': Permission denied\n";

		(bool elevated, string sudoOutput, string scriptError) = SudoErrors.Split(error);

		Assert.True(elevated);
		Assert.Equal("sudo: unable to resolve host web01\n", sudoOutput);
		Assert.Equal("rm: cannot remove '/x': Permission denied\n", scriptError);
		Assert.False(SudoErrors.Split("sudo: a password is required\n").Elevated);
	}

	[Theory]
	[InlineData(nameof(SudoFailure.IncorrectPassword), "sudo did not accept the password for abc@web01.")]
	[InlineData(nameof(SudoFailure.NotAllowed), "abc@web01 is not allowed to run commands as root with sudo.")]
	[InlineData(nameof(SudoFailure.NotInstalled), "sudo is not installed on web01.")]
	public void Describe_NamesTheAccount(string failure, string expected) =>
		Assert.Equal(expected, SudoErrors.Describe(Enum.Parse<SudoFailure>(failure), "abc@web01", ""));

	[Fact]
	public void Describe_Other_QuotesTheFirstLine() =>
		Assert.Equal("sudo failed: This service allows sftp connections only.", SudoErrors.Describe(SudoFailure.Other, "abc@web01", "\nThis service allows sftp connections only.\nmore"));

	[Fact]
	public void EnsureElevated_WithoutMarker_ThrowsElevationFailed()
	{
		RemoteFileSystemException exception = Assert.Throws<RemoteFileSystemException>(
			() => SudoRunner.EnsureElevated(1, "abc is not in the sudoers file.\n", "abc@web01", "/etc/hosts", out _));

		Assert.Equal(RemoteFileErrorKind.ElevationFailed, exception.Kind);
		Assert.Equal("abc@web01 is not allowed to run commands as root with sudo.", exception.Message);
	}

	[Theory]
	[InlineData(RemoteScripts.ExitNotFound, "", RemoteFileErrorKind.NotFound)]
	[InlineData(RemoteScripts.ExitAlreadyExists, "", RemoteFileErrorKind.AlreadyExists)]
	[InlineData(RemoteScripts.ExitDirectoryNotEmpty, "", RemoteFileErrorKind.DirectoryNotEmpty)]
	[InlineData(1, "rmdir: failed to remove '/srv/x': Directory not empty\n", RemoteFileErrorKind.DirectoryNotEmpty)]
	[InlineData(1, "mkdir: cannot create directory '/proc/x': Permission denied\n", RemoteFileErrorKind.PermissionDenied)]
	[InlineData(1, "chattr: Operation not permitted while setting flags\n", RemoteFileErrorKind.PermissionDenied)]
	[InlineData(1, "mv: cannot stat '/srv/a': No such file or directory\n", RemoteFileErrorKind.NotFound)]
	[InlineData(1, "cat: write error: No space left on device\n", RemoteFileErrorKind.Unknown)]
	public void EnsureSucceeded_MapsScriptFailures(int exitStatus, string scriptError, RemoteFileErrorKind expected)
	{
		RemoteCommandResult result = new(exitStatus, [.. RemoteOutput.Marker], "MOKATERM-ELEVATED\n" + scriptError);

		RemoteFileSystemException exception = Assert.Throws<RemoteFileSystemException>(
			() => SudoRunner.EnsureSucceeded(result, "abc@web01", "/srv/a", "/srv/b"));

		Assert.Equal(expected, exception.Kind);
	}

	[Fact]
	public void EnsureSucceeded_ReturnsThePayloadAfterTheMarker()
	{
		RemoteCommandResult result = new(0, [.. "motd\n"u8, .. RemoteOutput.Marker, .. "/srv/app\n"u8], "MOKATERM-ELEVATED\n");

		ReadOnlyMemory<byte> payload = SudoRunner.EnsureSucceeded(result, "abc@web01", "/srv/app");

		Assert.Equal("/srv/app", RemoteOutput.ReadLine(payload.Span));
	}
}
