using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Abstractions.Commands;

/// <summary>One command for several saved logins. The command line is printed as "(set)" only, since it can carry a token.</summary>
public sealed record CommandRunRequest
{
	/// <summary>Checked with <see cref="RemoteCommandRequest.Problem"/>.</summary>
	public required string Command { get; init; }

	/// <summary>The saved logins, in the order their rows appear. A login listed twice runs once.</summary>
	public required IReadOnlyList<Guid> ConnectionIds { get; init; }

	/// <summary>Parallel limit, per-host timeout, output cap and the prompt rule; the runner applies <see cref="CommandRunSettings.Clamped"/>.</summary>
	public CommandRunSettings Options { get; init; } = new();

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Command = ").Append(SecretText.Describe(Command))
			.Append(", Logins = ").Append(ConnectionIds.Count)
			.Append(", Options = ").Append(Options);
		return true;
	}
}
