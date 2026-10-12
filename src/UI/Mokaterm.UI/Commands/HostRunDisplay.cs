using System.Globalization;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Commands;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.UI.Commands;

/// <summary>Labels, colours and times for the hosts of a run across logins: a command run's, or the fleet overview's.</summary>
internal static class HostRunDisplay
{
	public static string Label(HostRunState state) => state switch
	{
		HostRunState.Queued => "QUEUED",
		HostRunState.Connecting => "CONNECTING",
		HostRunState.Running => "RUNNING",
		HostRunState.Done => "DONE",
		HostRunState.Failed => "FAILED",
		HostRunState.Skipped => "SKIPPED",
		_ => "CANCELLED",
	};

	/// <summary>
	/// A host whose work ran but came back with a problem (a non-zero exit) gets a warning rather than an error: the host
	/// was reached.
	/// </summary>
	public static MokaColor Color(HostRunResult host)
	{
		ArgumentNullException.ThrowIfNull(host);
		return host.State switch
		{
			HostRunState.Done => host.IsProblem ? MokaColor.Warning : MokaColor.Success,
			HostRunState.Failed => MokaColor.Error,
			HostRunState.Connecting or HostRunState.Running => MokaColor.Info,
			_ => MokaColor.Surface,
		};
	}

	/// <summary><c>exit 0</c>, <c>signal KILL</c>, or nothing for a host that did not run the command.</summary>
	public static string? Exit(CommandHostResult host)
	{
		ArgumentNullException.ThrowIfNull(host);
		return host switch
		{
			{ State: not HostRunState.Done } => null,
			{ ExitCode: { } code } => string.Create(CultureInfo.InvariantCulture, $"exit {code}"),
			{ ExitSignal: { Length: > 0 } signal } => $"signal {signal}",
			_ => "no exit status",
		};
	}

	/// <summary><c>0.4 s</c> and <c>12.3 s</c> under a minute, where a tenth tells commands apart, then <c>01:05</c>.</summary>
	public static string Duration(TimeSpan value) =>
		value.TotalSeconds < 60
			? string.Create(CultureInfo.CurrentCulture, $"{value.TotalSeconds:0.0} s")
			: DisplayFormat.Duration(value);

	/// <summary>The first line of a command, for a one-line summary; the rest is said to be there.</summary>
	public static string FirstLine(string command)
	{
		ArgumentNullException.ThrowIfNull(command);
		int end = command.IndexOfAny(['\r', '\n']);
		return end < 0 ? command : command[..end] + " ...";
	}
}
