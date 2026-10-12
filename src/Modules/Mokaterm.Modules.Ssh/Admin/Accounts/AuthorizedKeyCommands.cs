using System.Globalization;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Modules.Ssh.Admin.Accounts;

/// <summary>
/// Reading, adding and removing the keys in a user's <c>~/.ssh/authorized_keys</c>. The home folder comes from passwd
/// on the server, the file is never written through a symbolic link (as root that would write wherever the user points
/// it), and lines are matched by their key alone, so options and comments on the lines that stay are kept as they are.
/// Another user's file needs root; the login's own does not.
/// </summary>
internal static class AuthorizedKeyCommands
{
	/// <summary>The most of a file that is read. A file of keys is a few KB; past this the list says it is cut.</summary>
	public const int MaxReadBytes = 256 * 1024;

	private const int MaxCommentLength = 100;

	private const string RefuseLinks =
		"if [ -L \"$dir\" ] || [ -L \"$file\" ]; then echo \"$dir or the file in it is a symbolic link, so nothing is written through it.\" >&2; exit 1; fi";

	/// <summary>Reads <paramref name="user"/>'s file; <paramref name="asRoot"/> is needed for anyone but the login.</summary>
	public static AdminScript Read(string user, bool asRoot)
	{
		Require(user);
		return new AdminScript
		{
			Name = "keys.read",
			AsRoot = asRoot,
			Body = string.Join('\n',
			[
				.. Locate(user),
				AdminSections.Start("home"),
				"printf '%s\\n' \"$home\"",
				AdminSections.Start("state"),
				"if [ -z \"$home\" ] || [ ! -d \"$home\" ]; then echo no-home",
				"elif [ ! -x \"$home\" ]; then echo unreadable",
				"elif [ ! -e \"$dir\" ] && [ ! -L \"$dir\" ]; then echo no-folder",
				"elif [ ! -x \"$dir\" ]; then echo unreadable",
				"elif [ ! -e \"$file\" ] && [ ! -L \"$file\" ]; then echo no-file",
				"elif [ ! -r \"$file\" ]; then echo unreadable",
				"else echo present",
				"fi",
				"if [ -L \"$dir\" ] || [ -L \"$file\" ]; then " + AdminSections.Start("link") + "; fi",
				AdminSections.Start("folder-mode"),
				"stat -c '%a %U' -- \"$dir\" 2>/dev/null",
				AdminSections.Start("file-mode"),
				"stat -c '%a %U' -- \"$file\" 2>/dev/null",
				AdminSections.Start("size"),
				"if [ -f \"$file\" ] && [ -r \"$file\" ]; then wc -c < \"$file\"; fi",
				AdminSections.Start("keys"),
				string.Create(CultureInfo.InvariantCulture, $"if [ -f \"$file\" ] && [ -r \"$file\" ]; then head -c {MaxReadBytes} -- \"$file\"; fi"),
				"exit 0",
			]),
		};
	}

	public static AuthorizedKeysFile Parse(string output, string user)
	{
		AdminSections sections = AdminSections.Parse(output);
		string home = AdminSections.Lines(sections.Get("home")).FirstOrDefault()?.Trim() ?? "";
		AuthorizedKeysState state = (AdminSections.Lines(sections.Get("state")).FirstOrDefault()?.Trim()) switch
		{
			"present" => AuthorizedKeysState.Present,
			"no-file" => AuthorizedKeysState.NoFile,
			"no-folder" => AuthorizedKeysState.NoFolder,
			"no-home" => AuthorizedKeysState.NoHome,
			_ => AuthorizedKeysState.Unreadable,
		};

		long size = long.TryParse(AdminSections.Lines(sections.Get("size")).FirstOrDefault()?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0;
		return new AuthorizedKeysFile
		{
			State = state,
			Home = home,
			Keys = state == AuthorizedKeysState.Present ? AuthorizedKeysParser.Parse(sections.Get("keys")) : [],
			IsLink = sections.Has("link"),
			IsCut = size > MaxReadBytes,
			ModeProblem = ModeProblem(sections.Get("folder-mode"), "~/.ssh", user) ?? ModeProblem(sections.Get("file-mode"), "authorized_keys", user),
		};
	}

	/// <summary>
	/// Adds <paramref name="key"/> to <paramref name="user"/>'s file, creating <c>~/.ssh</c> (700) and the file (600)
	/// owned by the user when they are missing. A key the file already holds is left as it is.
	/// </summary>
	/// <param name="comment">What the line says after the key when the key itself carries no comment, such as its name in the keychain.</param>
	public static AdminAction Add(string user, string loginName, SshPublicKey key, string? comment)
	{
		Require(user);
		ArgumentNullException.ThrowIfNull(key);
		string line = LineFor(key, comment);
		AuthorizedKey parsed = AuthorizedKeysParser.ParseLine(line, 1) ?? throw new ArgumentException("The key is not one sshd would read.", nameof(key));
		bool asRoot = !string.Equals(user, loginName, StringComparison.Ordinal);
		return new AdminAction
		{
			Title = $"Add {DisplayName(parsed)} to {user}'s keys",
			Script = Change("keys.add", asRoot,
			[
				$"key={PosixShell.Quote(line)}",
				$"blob={PosixShell.Quote(parsed.Blob)}",
				.. Locate(user),
				"if [ -z \"$home\" ] || [ ! -d \"$home\" ]; then echo \"$user has no home folder to keep keys in.\" >&2; exit 1; fi",
				RefuseLinks,
				"group=$(id -gn \"$user\")",
				"if [ ! -d \"$dir\" ]; then",
				"\tmkdir -m 700 -- \"$dir\"",
				"\tchown \"$user:$group\" -- \"$dir\"",
				"fi",
				"if [ ! -e \"$file\" ]; then",
				"\t( umask 077; : > \"$file\" )",
				"\tchown \"$user:$group\" -- \"$file\"",
				"\tchmod 600 -- \"$file\"",
				"fi",
				"if awk -v b=\"$blob\" '{ for (i = 1; i <= NF; i++) if ($i == b) found = 1 } END { exit !found }' \"$file\"; then",
				"\techo 'The file already holds this key.'",
				"\texit 0",
				"fi",
				"if [ -s \"$file\" ] && [ -n \"$(tail -c 1 -- \"$file\")\" ]; then printf '\\n' >> \"$file\"; fi",
				"printf '%s\\n' \"$key\" >> \"$file\"",
			]),
			Description = asRoot
				? $"Appends the key to {user}'s ~/.ssh/authorized_keys as root, creating the folder (700) and the file (600) owned by {user} when they are missing. The other lines stay as they are."
				: "Appends the key to your ~/.ssh/authorized_keys, creating the folder (700) and the file (600) when they are missing. The other lines stay as they are.",
			ConfirmText = "Add key",
		};
	}

	/// <summary>
	/// Removes every line that holds <paramref name="key"/>'s key from <paramref name="user"/>'s file, keeping the file's
	/// owner and mode. Removing the key this session logged in with says so in red.
	/// </summary>
	public static AdminAction Remove(string user, SessionLogin login, AuthorizedKey key)
	{
		Require(user);
		ArgumentNullException.ThrowIfNull(login);
		ArgumentNullException.ThrowIfNull(key);
		if (AuthorizedKeysParser.DecodeBlob(key.Algorithm, key.Blob) is null)
		{
			throw new ArgumentException("The key is not one sshd would read.", nameof(key));
		}

		bool ownFile = string.Equals(user, login.UserName, StringComparison.Ordinal);
		bool sessionKey = ownFile && login.MayUseKey && string.Equals(login.KeyFingerprint, key.Fingerprint, StringComparison.Ordinal);
		bool maybeSessionKey = ownFile && login.MayUseKey && login.KeyFingerprint is null;
		return new AdminAction
		{
			Title = $"Remove {DisplayName(key)} from {user}'s keys",
			Script = Change("keys.remove", !ownFile,
			[
				$"blob={PosixShell.Quote(key.Blob)}",
				.. Locate(user),
				RefuseLinks,
				"if [ ! -f \"$file\" ]; then echo \"$user has no authorized_keys file.\" >&2; exit 1; fi",
				"kept=$(mktemp)",
				"trap 'rm -f \"$kept\"' EXIT",
				"awk -v b=\"$blob\" '{ keep = 1; for (i = 1; i <= NF; i++) if ($i == b) keep = 0; if (keep) print }' \"$file\" > \"$kept\"",

				// Written back in place, so the file keeps its owner, mode and security label.
				"cat \"$kept\" > \"$file\"",
			]),
			Description = "Removes every line that holds this key. The other lines, their options and comments, stay as they are.",
			Warning = sessionKey
				? "This session logged in with this key. Removing it refuses the next login with it, this app's included; this session stays open."
				: maybeSessionKey
					? "This session logged in with a key and this view cannot tell which. If it is this one, the next login with it is refused."
					: null,
			CutsConnection = sessionKey,
			Destructive = true,
			ConfirmText = sessionKey ? "Remove anyway" : "Remove key",
		};
	}

	/// <summary>
	/// The line written for <paramref name="key"/>: type, key and a comment. A key with no comment of its own gets
	/// <paramref name="comment"/>, kept to printable text on one line.
	/// </summary>
	public static string LineFor(SshPublicKey key, string? comment)
	{
		ArgumentNullException.ThrowIfNull(key);
		string[] words = key.AuthorizedKeysLine.Split((char[])[' ', '\t'], 3, StringSplitOptions.RemoveEmptyEntries);
		if (words.Length < 2)
		{
			throw new ArgumentException("The key has no key data.", nameof(key));
		}

		string label = Printable(words.Length > 2 ? words[2] : key.Comment);
		if (label.Length == 0)
		{
			label = Printable(comment);
		}

		return label.Length == 0 ? $"{words[0]} {words[1]}" : $"{words[0]} {words[1]} {label}";
	}

	/// <summary>A key as a line of text names it: its comment, else its type and fingerprint.</summary>
	public static string DisplayName(AuthorizedKey key)
	{
		ArgumentNullException.ThrowIfNull(key);
		return key.Comment.Length > 0 ? key.Comment : $"{key.Algorithm} {key.Fingerprint}";
	}

	/// <summary>Sets <c>user</c>, <c>home</c>, <c>dir</c> and <c>file</c>; the home comes from passwd, whatever the login's own is.</summary>
	private static string[] Locate(string user) =>
	[
		$"user={PosixShell.Quote(user)}",
		"home=$(getent passwd \"$user\" 2>/dev/null | cut -d: -f6)",
		"if [ -z \"$home\" ]; then home=$(awk -F: -v u=\"$user\" '$1 == u { print $6; exit }' /etc/passwd 2>/dev/null); fi",
		"dir=\"$home/.ssh\"",
		"file=\"$dir/authorized_keys\"",
	];

	private static AdminScript Change(string name, bool asRoot, string[] lines) => new()
	{
		Name = name,
		Body = string.Join('\n', ["set -e", .. lines]),
		AsRoot = asRoot,
		Timeout = AdminScript.ActionTimeout,
		OutputLimitBytes = AdminScript.ActionOutputBytes,
	};

	/// <summary>sshd's StrictModes: neither the folder nor the file may be writable by others, and both belong to the user or root.</summary>
	private static string? ModeProblem(string? stat, string what, string user)
	{
		string[] words = (AdminSections.Lines(stat).FirstOrDefault() ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (words.Length < 2 || words[0].Length is 0 or > 4 || !words[0].All(digit => digit is >= '0' and <= '7'))
		{
			return null;
		}

		// Group and other write, as sshd's own check reads the mode.
		if ((Convert.ToInt32(words[0], 8) & 0b000_010_010) != 0)
		{
			return $"{what} can be written by others, so sshd ignores the keys (StrictModes).";
		}

		return words[1] != user && words[1] != "root" ? $"{what} belongs to {words[1]}, so sshd ignores the keys (StrictModes)." : null;
	}

	private static string Printable(string? text)
	{
		string cleaned = new([.. (text ?? "").Where(character => !char.IsControl(character))]);
		cleaned = cleaned.Trim();
		return cleaned.Length > MaxCommentLength ? cleaned[..MaxCommentLength].TrimEnd() : cleaned;
	}

	private static void Require(string user)
	{
		if (AdminNames.CheckAccount(user) is { } problem)
		{
			throw new ArgumentException(problem, nameof(user));
		}
	}
}
