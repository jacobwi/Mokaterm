using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Sessions.Commands;

/// <summary>One command on several logins: each host runs it once over its own connection and keeps what it printed.</summary>
internal sealed class CommandRun : HostRun<CommandHostResult>, ICommandRun
{
	public CommandRun(HostRunner owner, string command, CommandRunSettings options)
		: base(owner, options) => Command = command;

	public string Command { get; }

	protected override string RunName => "command run";

	protected override string FailedWithoutReason => "The command could not run.";

	protected override string UnexpectedFailure => "The command could not run because of an unexpected error.";

	protected override string? WhyNot(ProtocolDescriptor? protocol) => CommandRunEligibility.WhyNot(protocol);

	protected override CommandHostResult Blank(Guid connectionId) => new()
	{
		ConnectionId = connectionId,
		Title = DeletedLogin,
		HostName = "",
		Address = "",
	};

	protected override CommandHostResult WithoutResult(CommandHostResult row) => row with
	{
		ExitCode = null,
		ExitSignal = null,
		Output = CommandOutputText.Empty,
		Error = CommandOutputText.Empty,
	};

	protected override string TimedOutWhileRunning(string seconds) => $"The command did not finish within {seconds} s.";

	protected override async Task<CommandHostResult> RunOnSessionAsync(IProtocolSession session, Func<CommandHostResult> started, CancellationToken cancellationToken)
	{
		IRemoteCommandFeature commands = session.GetFeature<IRemoteCommandFeature>()
			?? throw new RemoteCommandException("This connection cannot run commands.");

		CommandHostResult row = started();
		RemoteCommandOutcome outcome = await commands.RunAsync(
			new RemoteCommandRequest { Command = Command, OutputLimitBytes = Options.OutputLimitBytes },
			cancellationToken);

		return row with
		{
			ExitCode = outcome.ExitCode,
			ExitSignal = outcome.ExitSignal,
			Duration = outcome.Duration,
			Output = CommandOutputDecoder.Decode(outcome.Output),
			Error = CommandOutputDecoder.Decode(outcome.Error),
		};
	}
}
