using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Fleet;

namespace Mokaterm.Modules.Ssh.Admin.Survey;

/// <summary>
/// The fleet overview's read of an SSH machine: <see cref="SurveyCommands.Script"/> run as the login over one exec
/// channel, framed and read back the way the system view's scripts are. Public so a demo protocol can offer the same
/// read over its own command feature.
/// </summary>
public sealed class SshHostSummaryFeature : IHostSummaryFeature
{
	private readonly IRemoteCommandFeature _commands;

	public SshHostSummaryFeature(IRemoteCommandFeature commands)
	{
		ArgumentNullException.ThrowIfNull(commands);
		_commands = commands;
	}

	public async Task<HostSummary> ReadSummaryAsync(CancellationToken cancellationToken)
	{
		AdminOutcome outcome;

		// The account only names the login in messages about root, which a read never asks for.
		await using (CommandFeatureAdminShell shell = new(_commands, account: ""))
		{
			try
			{
				outcome = await shell.RunAsync(SurveyCommands.Script, elevated: false, input: null, cancellationToken);
			}
			catch (AdminException ex)
			{
				throw new RemoteCommandException(ex.Message, ex);
			}
		}

		HostSummary summary = SurveyCommands.Parse(outcome.Output);
		return outcome.IsTruncated
			? summary with { Notes = [.. summary.Notes, "The machine printed more than the read keeps, so the last parts may be missing."] }
			: summary;
	}
}
