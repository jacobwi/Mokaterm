using System.Text;

namespace Mokaterm.Abstractions.ServerAdmin;

/// <summary>How a <see cref="SystemChange"/> the user ran ended. Prints its title only.</summary>
public sealed record SystemChangeResult
{
	public required string Title { get; init; }

	/// <summary>How the script ended; null when it did not run to its end (see <see cref="Failure"/>).</summary>
	public SystemScriptResult? Outcome { get; init; }

	/// <summary>Why the script did not run to its end: sudo refused, the time ran out, the connection dropped.</summary>
	public string? Failure { get; init; }

	public bool Succeeded => Outcome?.Succeeded == true;

	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Title = ").Append(Title).Append(", Succeeded = ").Append(Succeeded);
		return true;
	}
}
