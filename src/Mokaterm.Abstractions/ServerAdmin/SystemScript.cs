using System.Text;
using System.Text.RegularExpressions;

namespace Mokaterm.Abstractions.ServerAdmin;

/// <summary>
/// A POSIX sh script a System view page runs on the server. The view adds what every script of its own gets:
/// <c>LC_ALL=C</c>, the sbin folders on <c>PATH</c>, the output marker that keeps a login banner out of the output, a
/// time limit, and the session's exec channel budget. <see cref="Body"/> is exactly what a change shows before it runs,
/// so every value in it must already be checked and quoted with <see cref="Commands.PosixShell.Quote"/>.
/// </summary>
public sealed partial record SystemScript
{
	/// <summary>
	/// What the script does, such as <c>fail2ban.status</c>: letters, digits, dots, dashes and underscores, and never a
	/// value from the user. Logs and the DevHost's demo server tell scripts apart by it.
	/// </summary>
	public required string Name { get; init; }

	/// <summary>The commands, exactly as a change shows them before running.</summary>
	public required string Body { get; init; }

	/// <summary>Runs as root: directly when the login is root, through sudo otherwise.</summary>
	public bool AsRoot { get; init; }

	/// <summary>How long it may run. Null takes the view's own limit for a read or a change; the most it allows is what it gives a package upgrade.</summary>
	public TimeSpan? Timeout { get; init; }

	/// <summary>Most stdout kept. Null takes the view's own cap for a read or a change; a larger number is cut to the read cap.</summary>
	public int? OutputLimitBytes { get; init; }

	/// <summary>Whether <paramref name="name"/> can name a script.</summary>
	public static bool IsName(string? name) => name is not null && NamePattern().IsMatch(name);

	// A body can hold a name the user typed; nothing that prints a record should carry it.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Name = ").Append(Name).Append(", AsRoot = ").Append(AsRoot);
		return true;
	}

	// The name ends the script's first line, so \z rather than $, which would let a line break through.
	[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant)]
	private static partial Regex NamePattern();
}
