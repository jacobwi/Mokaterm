using System.Text.RegularExpressions;

namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>
/// The strict patterns every name passes before it reaches a script. Quoting already keeps a value from becoming shell
/// syntax; these also keep it from becoming an option (a leading dash) or a path outside where it belongs, and they
/// give the user a reason in words instead of a tool's error.
/// </summary>
internal static partial class AdminNames
{
	/// <summary>Why <paramref name="name"/> is not a user or group name, or null. Lower and upper case, digits, dot, dash, underscore; 32 at most.</summary>
	public static string? CheckAccount(string? name) => Check(name, AccountPattern(), "User and group names start with a letter or an underscore and hold only letters, digits, dots, dashes and underscores, 32 at most.");

	/// <summary>A systemd unit or an init script: letters, digits and <c>:_.@\-</c>, the escaping systemd itself uses.</summary>
	public static string? CheckService(string? name) => Check(name, ServicePattern(), "Service names hold only letters, digits and the characters : _ . @ - \\, and start with a letter or digit.");

	/// <summary>A package name as apt, rpm, pacman and apk spell them, with an optional <c>:arch</c> for apt.</summary>
	public static string? CheckPackage(string? name) => Check(name, PackagePattern(), "Package names start with a letter or digit and hold only letters, digits and the characters . _ + - : @.");

	/// <summary>A time zone from the tz database, such as <c>Europe/Berlin</c> or <c>UTC</c>.</summary>
	public static string? CheckTimeZone(string? name)
	{
		if (Check(name, TimeZonePattern(), "Time zones look like Europe/Berlin or UTC.") is { } problem)
		{
			return problem;
		}

		return name!.Split('/').Any(part => part is "." or "..") ? "Time zones look like Europe/Berlin or UTC." : null;
	}

	/// <summary>A network interface: 15 characters at most (IFNAMSIZ), no slash or space.</summary>
	public static string? CheckInterface(string? name) => Check(name, InterfacePattern(), "Interface names hold only letters, digits and the characters . _ - :, 15 at most.");

	/// <summary>A host name as hostnamectl takes it: dot-separated labels of letters, digits and dashes, 253 at most.</summary>
	public static string? CheckHostName(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return "Type a host name.";
		}

		if (name.Length > 253)
		{
			return "A host name holds at most 253 characters.";
		}

		foreach (string label in name.Split('.'))
		{
			if (!HostLabelPattern().IsMatch(label))
			{
				return "Each part of a host name holds 1 to 63 letters, digits and dashes, and does not start or end with a dash.";
			}
		}

		return null;
	}

	/// <summary>A login shell: an absolute path of plain characters.</summary>
	public static string? CheckShellPath(string? path) => Check(path, ShellPathPattern(), "A shell is an absolute path such as /bin/bash.");

	/// <summary>The comment (GECOS) field: printable text without a colon, which separates passwd fields, or a comma, which separates GECOS ones.</summary>
	public static string? CheckComment(string? comment)
	{
		if (string.IsNullOrEmpty(comment))
		{
			return null;
		}

		if (comment.Length > 64)
		{
			return "Keep the full name to 64 characters.";
		}

		return comment.Any(character => char.IsControl(character) || character is ':' or ',')
			? "The full name cannot hold a colon, a comma or a control character."
			: null;
	}

	/// <summary>A file under <c>/var/log</c>, named the way <c>find</c> listed it, never climbing out.</summary>
	public static string? CheckLogPath(string? path)
	{
		if (Check(path, LogPathPattern(), "Only files under /var/log can be read here.") is { } problem)
		{
			return problem;
		}

		return path!.Split('/').Any(part => part is "." or "..") ? "Only files under /var/log can be read here." : null;
	}

	/// <summary>
	/// A folder to measure: an absolute path without <c>.</c> or <c>..</c> parts, control characters or a line break,
	/// which would split du's output. Anything else is allowed, since a folder may be named anything.
	/// </summary>
	public static string? CheckFolderPath(string? path)
	{
		if (string.IsNullOrEmpty(path) || !path.StartsWith('/'))
		{
			return "Type an absolute path, such as /var/log.";
		}

		if (path.Length > 4096 || path.Any(char.IsControl))
		{
			return "A folder path cannot hold control characters or line breaks, or be longer than 4096 characters.";
		}

		return path.Split('/').Any(part => part is "." or "..") ? "Type the folder's own path, without . or .. in it." : null;
	}

	/// <summary>A block device as lsblk names it under <c>/dev</c>: sda, nvme0n1, mmcblk0, cciss!c0d0.</summary>
	public static string? CheckBlockDevice(string? name) => Check(name, BlockDevicePattern(), "Device names hold only letters, digits and the characters . _ - : ! +.");

	/// <summary>A new password for chpasswd: one line, no NUL.</summary>
	public static string? CheckPassword(string? password)
	{
		if (string.IsNullOrEmpty(password))
		{
			return "Type a password.";
		}

		return password.Any(character => character is '\n' or '\r' or '\0') ? "A password cannot hold a line break." : null;
	}

	private static string? Check(string? value, Regex pattern, string problem)
	{
		if (string.IsNullOrEmpty(value))
		{
			return "This cannot be empty.";
		}

		return pattern.IsMatch(value) ? null : problem;
	}

	[GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.-]{0,31}$", RegexOptions.CultureInvariant)]
	private static partial Regex AccountPattern();

	[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9:_.@\\-]{0,254}$", RegexOptions.CultureInvariant)]
	private static partial Regex ServicePattern();

	[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+:@-]{0,127}$", RegexOptions.CultureInvariant)]
	private static partial Regex PackagePattern();

	[GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_+-]{0,40}(/[A-Za-z0-9][A-Za-z0-9_+-]{0,40}){0,2}$", RegexOptions.CultureInvariant)]
	private static partial Regex TimeZonePattern();

	[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.:-]{0,14}$", RegexOptions.CultureInvariant)]
	private static partial Regex InterfacePattern();

	[GeneratedRegex(@"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)$", RegexOptions.CultureInvariant)]
	private static partial Regex HostLabelPattern();

	[GeneratedRegex(@"^/[A-Za-z0-9_.+/-]{1,255}$", RegexOptions.CultureInvariant)]
	private static partial Regex ShellPathPattern();

	[GeneratedRegex(@"^/var/log/[A-Za-z0-9_.@+-][A-Za-z0-9_.@+/-]{0,255}$", RegexOptions.CultureInvariant)]
	private static partial Regex LogPathPattern();

	[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.:!+-]{0,63}$", RegexOptions.CultureInvariant)]
	private static partial Regex BlockDevicePattern();
}
