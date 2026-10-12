using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Where docker commands run: an exec channel on the session's connection, as the login or through sudo. The interface
/// is what lets the client, the detection and the pages be tested without a server.
/// </summary>
internal interface IDockerShell
{
	/// <summary>True when every command runs through sudo.</summary>
	bool IsElevated { get; }

	/// <summary>Runs <paramref name="call"/> to its end, whatever it exits with, within the call's own time limit.</summary>
	/// <exception cref="DockerCommandException">
	/// The command did not run to its end: the connection closed, the time ran out, or sudo refused it.
	/// </exception>
	Task<DockerOutput> RunAsync(DockerCall call, CancellationToken cancellationToken);
}
