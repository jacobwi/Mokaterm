using System.Text;

namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>
/// A change the system view offers: a title, the script, and what the confirmation says around it. Nothing runs until
/// the user has seen <see cref="AdminScript.Body"/> and agreed.
/// </summary>
internal sealed record AdminAction
{
	/// <summary>Short and specific, such as "Restart nginx.service".</summary>
	public required string Title { get; init; }

	public required AdminScript Script { get; init; }

	/// <summary>What happens, in a sentence or two, when the title does not say it all.</summary>
	public string? Description { get; init; }

	/// <summary>What could go wrong for this session or this machine: a lockout, a dropped connection, a boot that fails.</summary>
	public string? Warning { get; init; }

	/// <summary>The warning is about losing this SSH connection (a network, firewall or sshd change), so it shows in red.</summary>
	public bool CutsConnection { get; init; }

	/// <summary>Removes or stops something, so the confirmation is styled as dangerous.</summary>
	public bool Destructive { get; init; }

	/// <summary>The confirm button, such as "Restart" or "Delete user".</summary>
	public string ConfirmText { get; init; } = "Run";

	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Script = ").Append(Script.Name).Append(", Destructive = ").Append(Destructive);
		return true;
	}
}
