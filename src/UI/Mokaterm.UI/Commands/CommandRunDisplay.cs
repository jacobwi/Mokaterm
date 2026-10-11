using System.Globalization;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Commands;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.UI.Commands;

/// <summary>Labels, colours and times for the hosts of a command run.</summary>
internal static class CommandRunDisplay
{
	public static string Label(CommandHostState state) => state switch
	{
		CommandHostState.Queued => "QUEUED",
		CommandHostState.Connecting => "CONNECTING",
		CommandHostState.Running => "RUNNING",
		CommandHostState.Done => "DONE",
		CommandHostState.Failed => "FAILED",
		CommandHostState.Skipped => "SKIPPED",
		_ => "CANCELLED",
	};

	/// <summary>A non-zero exit is still done, so it gets a warning rather than an error: the host was reached.</summary>
	public static MokaColor Color(CommandHostResult host)
	{
		ArgumentNullException.ThrowIfNull(host);
		return host.State switch
		{
			CommandHostState.Done => host.ExitCode == 0 ? MokaColor.Success : MokaColor.Warning,
			CommandHostState.Failed => MokaColor.Error,
			CommandHostState.Connecting or CommandHostState.Running => MokaColor.Info,
			_ => MokaColor.Surface,
		};
	}

	/// <summary><c>exit 0</c>, <c>signal KILL</c>, or nothing for a host that did not run the command.</summary>
	public static string? Exit(CommandHostResult host)
	{
		ArgumentNullException.ThrowIfNull(host);
		return host switch
		{
			{ State: not CommandHostState.Done } => null,
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
