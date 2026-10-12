using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Reads a failed docker command's exit status and stderr. The client has printed the same few messages for years in
/// two wordings, the old "Docker daemon" one and the newer "docker API" one, and both are matched.
/// </summary>
internal static class DockerErrors
{
	/// <summary>What a POSIX shell exits with when it cannot find the program.</summary>
	public const int CommandNotFoundExitStatus = 127;

	private static readonly string[] NotInstalledMarkers =
	[
		"docker: not found",
		"docker: command not found",
		"docker-compose: not found",
		"docker-compose: command not found",
		"No such file or directory",
	];

	private static readonly string[] DaemonMarkers =
	[
		"docker daemon",
		"docker api",
		"docker.sock",
		"docker_engine",
	];

	private static readonly string[] UnreachableMarkers =
	[
		"cannot connect to the docker daemon",
		"failed to connect to the docker api",
		"is the docker daemon running",
		"error during connect",
	];

	private static readonly string[] NotFoundMarkers =
	[
		"no such container",
		"no such image",
		"no such volume",
		"no such network",
		"no such object",
		"not found: manifest unknown",
	];

	private const string ComposeMissing = "is not a docker command";

	public static DockerFailure Classify(int? exitStatus, string? error)
	{
		string text = error ?? "";
		if (text.Contains("permission denied", StringComparison.OrdinalIgnoreCase) && ContainsAny(text, DaemonMarkers))
		{
			return DockerFailure.PermissionDenied;
		}

		if (ContainsAny(text, UnreachableMarkers))
		{
			return DockerFailure.DaemonUnreachable;
		}

		if (ContainsAny(text, NotFoundMarkers))
		{
			return DockerFailure.NotFound;
		}

		if (exitStatus == CommandNotFoundExitStatus
			|| text.Contains(ComposeMissing, StringComparison.OrdinalIgnoreCase)
			|| (exitStatus is not 0 && ContainsAny(text, NotInstalledMarkers) && text.Contains("docker", StringComparison.OrdinalIgnoreCase)))
		{
			return DockerFailure.NotInstalled;
		}

		return DockerFailure.Other;
	}

	/// <summary>A sentence for the user. <paramref name="host"/> names the server; <paramref name="error"/> is quoted for the rest.</summary>
	public static string Describe(DockerFailure failure, string host, string? error) => failure switch
	{
		DockerFailure.NotInstalled => $"Docker is not installed on {host}, or it is not on the login's PATH.",
		DockerFailure.PermissionDenied => $"This account may not use Docker on {host}: the daemon's socket only lets in root and the docker group.",
		DockerFailure.DaemonUnreachable => $"The Docker daemon on {host} is not answering. {Quote(error)}".TrimEnd(),
		DockerFailure.NotFound => RemoteText.FirstLine(error) is { Length: > 0 } line ? line : "It is gone.",
		_ => RemoteText.FirstLine(error) is { Length: > 0 } other ? other : "docker failed without saying why.",
	};

	private static string Quote(string? error) =>
		RemoteText.FirstLine(error) is { Length: > 0 } line ? $"It said: {line}" : "";

	private static bool ContainsAny(string text, string[] markers) =>
		markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
