using Mokaterm.Abstractions.Containers;

namespace Mokaterm.UI.Common.ToolPages;

/// <summary>
/// What a page of the Docker manager gets from the manager around it, cascaded to every page: the docker host, whether
/// commands go through sudo, the compose it found, docker and compose calls on the session's connection, and the other
/// pages.
/// </summary>
/// <remarks>
/// Every call is an exec channel from the connection's budget, as the login or through sudo, whichever the manager uses
/// at that moment. Call members from the renderer's thread.
/// </remarks>
public interface IDockerPageContext
{
	/// <summary>The docker host, for messages.</summary>
	string Host { get; }

	/// <summary>Every command runs through sudo.</summary>
	bool IsElevated { get; }

	/// <summary>The compose that compose commands run, or null when the server has none.</summary>
	DockerComposeInfo? Compose { get; }

	/// <summary>Opens another page of the manager by its id, such as <c>containers</c>.</summary>
	void Navigate(string pageId);

	/// <summary>The command as a confirmation shows it, which is exactly what runs: <c>sudo</c> in front when the manager uses it.</summary>
	/// <exception cref="ArgumentException">A word holds NUL, or the command is compose and the server has none.</exception>
	string Display(DockerCommand command);

	/// <summary>Runs <paramref name="command"/> and hands back how it ended, whatever its exit.</summary>
	/// <exception cref="DockerOperationException">The command did not run to its end.</exception>
	/// <exception cref="ArgumentException">A word holds NUL, or the command is compose and the server has none.</exception>
	Task<DockerOutput> RunAsync(DockerCommand command, CancellationToken cancellationToken);

	/// <summary>Runs a read that must succeed and hands back its stdout.</summary>
	/// <exception cref="DockerOperationException">It did not run, or exited with anything but 0; the message says why in words.</exception>
	Task<string> ReadAsync(DockerCommand command, CancellationToken cancellationToken);

	/// <summary>
	/// Notes a change the user started from a page until the handle is disposed, so the manager's health watch does not
	/// report what it did to a container. <see cref="DockerPageBase.RunChangeAsync(IReadOnlyList{DockerCommand}, DockerChangePrompt, Func{Task})"/> does this itself.
	/// </summary>
	IDisposable BeginChange(IReadOnlyList<DockerCommand> commands);
}
