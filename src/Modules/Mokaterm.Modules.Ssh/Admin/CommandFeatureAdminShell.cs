using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;

namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>
/// The system view over a session that offers only <see cref="IRemoteCommandFeature"/>: every script runs as the login,
/// nothing runs as root and nothing goes in on stdin, so the view reads and offers no changes that need either.
/// </summary>
internal sealed class CommandFeatureAdminShell : IAdminShell
{
	private readonly IRemoteCommandFeature _commands;

	/// <param name="loginMethod">How the session logged in, as its connection says, or null when nothing says.</param>
	public CommandFeatureAdminShell(IRemoteCommandFeature commands, string account, AuthenticationMethod? loginMethod = null)
	{
		_commands = commands;
		Account = account;
		LoginMethod = loginMethod;
	}

	public string? ElevationBlockedReason => "This session cannot run commands as root.";

	public bool HasRoot => false;

	public string Account { get; }

	public AuthenticationMethod? LoginMethod { get; }

	public bool SupportsInput => false;

	public Task EnableRootAsync(CancellationToken cancellationToken) => throw new AdminException(ElevationBlockedReason!);

	public void DisableRoot()
	{
	}

	public async Task<AdminOutcome> RunAsync(AdminScript script, bool elevated, AdminInput? input, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(script);
		if (elevated)
		{
			throw new AdminException(ElevationBlockedReason!);
		}

		if (input is not null || script.ReadsInput)
		{
			throw new AdminException("This session cannot send input to a command.");
		}

		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(script.Timeout);
		string command = PosixShell.ScriptCommand(AdminScriptText.Compose(script), []);
		try
		{
			RemoteCommandOutcome outcome = await _commands.RunAsync(AdminScriptText.Request(script, command), deadline.Token);
			return AdminScriptText.ToOutcome(outcome, outcome.Error.ToText());
		}
		catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
		{
			throw new AdminException(AdminTexts.TimedOut(script.Timeout), ex);
		}
		catch (RemoteCommandException ex)
		{
			throw new AdminException(ex.Message, ex);
		}
	}

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
