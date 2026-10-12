using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>What docker says when an exec never reaches the script, as the kind the file browser reacts to.</summary>
public sealed class ContainerFileErrorsTests
{
	[Theory]
	[InlineData("OCI runtime exec failed: exec failed: unable to start container process: exec: \"sh\": executable file not found in $PATH: unknown")]
	[InlineData("OCI runtime exec failed: exec failed: container_linux.go:380: starting container process caused: exec: \"sh\": executable file not found in $PATH: unknown")]
	[InlineData("Error: crun: executable file `sh` not found in $PATH: No such file or directory: OCI runtime attempted to invoke a command that was not found")]
	[InlineData("OCI runtime exec failed: exec failed: unable to start container process: exec: \"sh\": stat sh: no such file or directory: unknown")]
	public void AnImageWithoutAShell_IsNotSupported_AndSaysWhy(string error)
	{
		RemoteFileSystemException failure = ContainerFileErrors.FromExec(Output(126, error), "api-1", "build-01", "/");

		Assert.Equal(RemoteFileErrorKind.NotSupported, failure.Kind);
		Assert.Equal("api-1 has no shell (sh), which browsing its files needs. Images built from scratch and distroless images have none.", failure.Message);
	}

	// Docker missing on the host also says "No such file or directory", but without the runtime's prefix.
	[Fact]
	public void DockerItselfMissing_IsNotReadAsAMissingShell()
	{
		RemoteFileSystemException failure = ContainerFileErrors.FromExec(Output(127, "sh: 1: docker: not found"), "web-1", "build-01", "/");

		Assert.Equal(RemoteFileErrorKind.NotSupported, failure.Kind);
		Assert.Contains("Docker is not installed on build-01", failure.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("Error response from daemon: container 3f2a9c1b is not running", "web-1 is not running, so its files cannot be reached.")]
	[InlineData("Error response from daemon: Container 3f2a9c1b is paused, unpause the container before exec", "web-1 is paused. Unpause it to reach its files.")]
	[InlineData("Error response from daemon: Container 3f2a9c1b is restarting, wait until the container is running", "web-1 is restarting. Try again once it runs.")]
	[InlineData("Error response from daemon: No such container: web-1", "There is no container named web-1 any more.")]
	public void AContainerThatIsNotThere_ReadsAsALostConnection(string error, string message)
	{
		RemoteFileSystemException failure = ContainerFileErrors.FromExec(Output(1, error), "web-1", "build-01", "/etc");

		Assert.Equal(RemoteFileErrorKind.ConnectionLost, failure.Kind);
		Assert.Equal(message, failure.Message);
		Assert.Equal("/etc", failure.Path);
	}

	[Fact]
	public void TheDaemonsSocket_RefusingTheLogin_IsAPermissionError()
	{
		RemoteFileSystemException failure = ContainerFileErrors.FromExec(
			Output(1, "permission denied while trying to connect to the Docker daemon socket at unix:///var/run/docker.sock"),
			"web-1",
			"build-01",
			"/");

		Assert.Equal(RemoteFileErrorKind.PermissionDenied, failure.Kind);
	}

	[Theory]
	[InlineData((int)DockerFailure.ElevationFailed, RemoteFileErrorKind.ElevationFailed)]
	[InlineData((int)DockerFailure.ConnectionLost, RemoteFileErrorKind.ConnectionLost)]
	[InlineData((int)DockerFailure.Other, RemoteFileErrorKind.Unknown)]
	public void ACommandThatNeverFinished_KeepsItsMessage(int docker, RemoteFileErrorKind expected)
	{
		RemoteFileSystemException failure = ContainerFileErrors.FromCommand(new DockerCommandException((DockerFailure)docker,"docker did not finish within 30 s, so it was stopped."), "/srv");

		Assert.Equal(expected, failure.Kind);
		Assert.Equal("docker did not finish within 30 s, so it was stopped.", failure.Message);
	}

	private static DockerOutput Output(int exitCode, string error) => new() { Command = "docker exec web-1 sh -c ...", ExitCode = exitCode, Error = error + "\n" };
}
