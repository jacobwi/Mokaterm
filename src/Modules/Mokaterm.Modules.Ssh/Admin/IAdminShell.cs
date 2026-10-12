using Mokaterm.Abstractions.Connections;

namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>
/// Where the system view's scripts run. SSH sessions run them over exec channels and through the module's sudo path;
/// a session that only offers <see cref="Mokaterm.Abstractions.Commands.IRemoteCommandFeature"/> runs them as the
/// login and nothing more.
/// </summary>
internal interface IAdminShell : IAsyncDisposable
{
	/// <summary>Why this session can never run anything as root, or null when sudo may be asked.</summary>
	string? ElevationBlockedReason { get; }

	/// <summary>True while sudo has accepted the login and the runner is held.</summary>
	bool HasRoot { get; }

	/// <summary>The login as <c>user@host</c>, for messages.</summary>
	string Account { get; }

	/// <summary>
	/// How the session logged in, which decides whether turning a login method off on the server locks this login out.
	/// Null when the session does not say.
	/// </summary>
	AuthenticationMethod? LoginMethod => null;

	/// <summary>Whether a script can be handed input on stdin, which a crontab and a new password need.</summary>
	bool SupportsInput { get; }

	/// <summary>Gets sudo ready: without a password when it allows that, else a remembered one, the login's, or a prompt.</summary>
	/// <exception cref="AdminException">sudo refused or the prompt was cancelled.</exception>
	Task EnableRootAsync(CancellationToken cancellationToken);

	/// <summary>Lets go of sudo. A command already running as root finishes first.</summary>
	void DisableRoot();

	/// <summary>
	/// Runs <paramref name="script"/> to its end, under its timeout and output cap. <paramref name="elevated"/> goes
	/// through sudo, which <see cref="EnableRootAsync"/> must have readied. <paramref name="input"/> is required when
	/// the script reads stdin and refused otherwise; the caller keeps it.
	/// </summary>
	/// <exception cref="AdminException">The script could not run to its end.</exception>
	Task<AdminOutcome> RunAsync(AdminScript script, bool elevated, AdminInput? input, CancellationToken cancellationToken);
}
