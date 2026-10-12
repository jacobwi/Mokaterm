using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The tab "Open shell" opens for a container: the session's own login again, not saved, whose login shell gives way to
/// <c>docker exec</c> as soon as it starts. SSH.NET opens a pseudo-terminal only together with a shell (its channel type
/// is internal, and its public API offers exec without a terminal or a shell with one, never exec with one), so the
/// command goes in as the login's startup command, with exec in front so that leaving the container ends the session.
/// </summary>
internal static class DockerShellTab
{
	/// <param name="login">The session's login. The copy keeps its credential, so the tab signs in the same way.</param>
	/// <param name="shell"><c>bash</c> or <c>sh</c>, whichever the container has.</param>
	/// <param name="elevated">True while the manager runs docker through sudo, which then asks in the new terminal.</param>
	/// <exception cref="ArgumentException">The container name or the shell breaks a rule.</exception>
	public static (ConnectionProfile Connection, SessionOpenOptions Options) For(ConnectionProfile login, string container, string shell, bool elevated)
	{
		ArgumentNullException.ThrowIfNull(login);
		SshConnectionOptions options = SshConnectionOptions.FromOptions(login.Options) with
		{
			StartupCommand = DockerCommands.ShellStartupCommand(container, shell, elevated),

			// The login's own tab already holds its saved tunnels; a second copy would only fail to bind the same ports.
			Tunnels = [],
		};

		ConnectionProfile connection = login with
		{
			Id = Guid.NewGuid(),
			Label = container,
			Options = options.ApplyTo(login.Options),
		};

		return (connection, new SessionOpenOptions { Title = container });
	}
}
