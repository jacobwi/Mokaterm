using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Abstractions.ServerAdmin;

/// <summary>
/// A change a System view page offers. The view shows its title, its exact commands, whether it runs as root and what
/// it sends on stdin, and runs it only once the user agreed. One change runs at a time on a session, and its result
/// stays at the bottom of the view until the next one.
/// </summary>
public sealed record SystemChange
{
	/// <summary>Short and specific, such as "Unban 203.0.113.4 in sshd".</summary>
	public required string Title { get; init; }

	public required SystemScript Script { get; init; }

	/// <summary>What happens, in a sentence or two, when the title does not say it all.</summary>
	public string? Description { get; init; }

	/// <summary>What could go wrong for this session or this machine: a lockout, a dropped connection, a boot that fails.</summary>
	public string? Warning { get; init; }

	/// <summary>The warning is about losing this SSH connection, so it shows in red.</summary>
	public bool CutsConnection { get; init; }

	/// <summary>Removes or stops something, so the confirmation is styled as dangerous.</summary>
	public bool Destructive { get; init; }

	/// <summary>The confirm button, such as "Unban".</summary>
	public string ConfirmText { get; init; } = "Run";

	/// <summary>
	/// What the script reads on stdin, such as a new password or a file's content; null sends nothing. It never goes on a
	/// command line, and through sudo it follows the password line.
	/// </summary>
	public string? Input { get; init; }

	/// <summary>What the review shows instead of <see cref="Input"/>, for a secret; null shows the input itself.</summary>
	public string? InputPreview { get; init; }

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Title = ").Append(Title)
			.Append(", Script = ").Append(Script.Name)
			.Append(", Destructive = ").Append(Destructive)
			.Append(", Input = ").Append(SecretText.Describe(Input));
		return true;
	}
}
