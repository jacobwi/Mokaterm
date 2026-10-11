using System.Text;

namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>A change that ran, or is running, as the result panel shows it. Prints its title only.</summary>
internal sealed record AdminActionResult
{
	public required string Title { get; init; }

	/// <summary>What was run, as the confirmation showed it.</summary>
	public required string Commands { get; init; }

	public bool AsRoot { get; init; }

	public DateTimeOffset StartedAt { get; init; }

	/// <summary>Null while running, and when the command never ran to its end (see <see cref="Failure"/>).</summary>
	public AdminOutcome? Outcome { get; init; }

	/// <summary>Why the command did not run to its end: sudo refused, the time ran out, the connection dropped.</summary>
	public string? Failure { get; init; }

	public bool IsRunning => Outcome is null && Failure is null;

	public bool Succeeded => Outcome?.Succeeded == true;

	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Title = ").Append(Title).Append(", Succeeded = ").Append(Succeeded);
		return true;
	}
}
