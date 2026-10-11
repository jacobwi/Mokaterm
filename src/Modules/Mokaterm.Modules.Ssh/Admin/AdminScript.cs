using System.Text;

namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>
/// One POSIX sh script for the system view: the lines that run, whether they run as root, and how long and how much
/// output they get. Every value in <see cref="Body"/> was validated and quoted when the script was built, so what an
/// action shows before it runs is exactly what runs.
/// </summary>
internal sealed record AdminScript
{
	/// <summary>How long a read gets. Listing a few thousand packages or units is well inside it.</summary>
	public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

	/// <summary>How long an ordinary change gets: a service restart, a user, a firewall rule.</summary>
	public static readonly TimeSpan ActionTimeout = TimeSpan.FromMinutes(2);

	/// <summary>How long a package transaction gets. Stopping one halfway leaves the package database for a human to repair.</summary>
	public static readonly TimeSpan PackageTimeout = TimeSpan.FromMinutes(30);

	/// <summary>Most output a read keeps. A package list of several thousand entries is a few hundred KB.</summary>
	public const int ReadOutputBytes = 4 * 1024 * 1024;

	/// <summary>Most output an action keeps for the result panel.</summary>
	public const int ActionOutputBytes = 1024 * 1024;

	/// <summary>What the script does, such as <c>services.list</c>. A constant: it never carries a value from the user.</summary>
	public required string Name { get; init; }

	/// <summary>The commands, exactly as an action shows them before running.</summary>
	public required string Body { get; init; }

	/// <summary>Runs as root: directly when the login is root, through sudo otherwise.</summary>
	public bool AsRoot { get; init; }

	public TimeSpan Timeout { get; init; } = ReadTimeout;

	public int OutputLimitBytes { get; init; } = ReadOutputBytes;

	/// <summary>The script reads what the caller hands over on stdin (a crontab, a password for chpasswd).</summary>
	public bool ReadsInput { get; init; }

	// A body can hold a host name or a package name the user typed; nothing that prints a record should carry it.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Name = ").Append(Name).Append(", AsRoot = ").Append(AsRoot);
		return true;
	}
}
