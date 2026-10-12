using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Reads a docker exec that never got as far as the module's script: the container is gone, stopped or paused, has no
/// shell, or docker itself refused. Each becomes the <see cref="RemoteFileSystemException"/> the file browser reacts to,
/// with a sentence that names the container. What the script itself ran into is read by
/// <see cref="Shell.RemoteOutput.Require"/> instead.
/// </summary>
internal static class ContainerFileErrors
{
	// runc says exec: "sh": executable file not found in $PATH, crun says executable file `sh` not found, and an image
	// whose sh is a dangling link says no such file or directory, each after an "OCI runtime" prefix.
	private static readonly string[] MissingShellMarkers = ["executable file not found", "not found in $path"];

	/// <summary>The failure of an exec whose output never reached the script's output marker.</summary>
	/// <param name="host">The docker host, for the daemon's own failures.</param>
	/// <param name="path">The path the operation was about.</param>
	public static RemoteFileSystemException FromExec(DockerOutput output, string container, string host, string path)
	{
		ArgumentNullException.ThrowIfNull(output);
		string error = output.Error;
		if (IsMissingShell(error))
		{
			return new RemoteFileSystemException(
				RemoteFileErrorKind.NotSupported,
				$"{container} has no shell (sh), which browsing its files needs. Images built from scratch and distroless images have none.",
				path);
		}

		if (Has(error, "is paused"))
		{
			return new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, $"{container} is paused. Unpause it to reach its files.", path);
		}

		if (Has(error, "is restarting"))
		{
			return new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, $"{container} is restarting. Try again once it runs.", path);
		}

		if (Has(error, "is not running"))
		{
			return new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, $"{container} is not running, so its files cannot be reached.", path);
		}

		DockerFailure failure = DockerErrors.Classify(output.ExitCode, error);
		return failure switch
		{
			DockerFailure.NotFound => new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, $"There is no container named {container} any more.", path),
			DockerFailure.PermissionDenied => new RemoteFileSystemException(RemoteFileErrorKind.PermissionDenied, DockerErrors.Describe(failure, host, error), path),
			DockerFailure.DaemonUnreachable => new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, DockerErrors.Describe(failure, host, error), path),
			DockerFailure.NotInstalled => new RemoteFileSystemException(RemoteFileErrorKind.NotSupported, DockerErrors.Describe(failure, host, error), path),
			_ => new RemoteFileSystemException(RemoteFileErrorKind.Unknown, DockerErrors.Describe(failure, host, error), path),
		};
	}

	/// <summary>The failure of a command that never finished: the connection went, the time ran out, or sudo refused.</summary>
	public static RemoteFileSystemException FromCommand(DockerCommandException exception, string path)
	{
		ArgumentNullException.ThrowIfNull(exception);
		RemoteFileErrorKind kind = exception.Failure switch
		{
			DockerFailure.ElevationFailed => RemoteFileErrorKind.ElevationFailed,
			DockerFailure.ConnectionLost => RemoteFileErrorKind.ConnectionLost,
			_ => RemoteFileErrorKind.Unknown,
		};

		return new RemoteFileSystemException(kind, exception.Message, path, exception);
	}

	/// <summary>True when docker could not start <c>sh</c> in the container at all.</summary>
	public static bool IsMissingShell(string? error)
	{
		string text = error ?? "";
		return MissingShellMarkers.Any(marker => Has(text, marker))
			|| (Has(text, "OCI runtime") && (Has(text, "not found") || Has(text, "no such file or directory")));
	}

	private static bool Has(string text, string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
