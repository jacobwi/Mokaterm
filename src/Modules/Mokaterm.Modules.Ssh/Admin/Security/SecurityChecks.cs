using System.Globalization;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Modules.Ssh.Admin.Network;

namespace Mokaterm.Modules.Ssh.Admin.Security;

/// <summary>
/// Turns what the security page read into checks, each with a level and, where a change is safe to offer, the change.
/// A change that turns off the way this session logged in is still offered, but marked as cutting the connection, with
/// a warning that says why.
/// </summary>
internal static class SecurityChecks
{
	/// <summary>sshd's own defaults, for keywords the files leave out when sshd -T could not say.</summary>
	private static readonly Dictionary<string, string> Defaults = new(StringComparer.Ordinal)
	{
		["passwordauthentication"] = "yes",
		["kbdinteractiveauthentication"] = "yes",
		["permitrootlogin"] = "prohibit-password",
		["pubkeyauthentication"] = "yes",
		["x11forwarding"] = "no",
		["maxauthtries"] = "6",
		["usepam"] = "no",
	};

	/// <summary>The most authentication attempts a connection gets before the security page suggests fewer.</summary>
	public const int MaxAuthTriesLimit = 4;

	/// <param name="securityUpdates">How many pending updates fix security problems, or null while unknown.</param>
	/// <param name="securityUpdatesNote">Why the count is unknown, when it is.</param>
	public static IReadOnlyList<SecurityCheck> Build(SecuritySnapshot snapshot, SessionLogin login, int? securityUpdates, string? securityUpdatesNote)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(login);
		List<SecurityCheck> checks = [.. Sshd(snapshot.Sshd, login)];
		checks.Add(Updates(securityUpdates, securityUpdatesNote));
		checks.Add(Fail2Ban(snapshot.Fail2Ban));
		checks.Add(Logins(snapshot));
		checks.Add(Ports(snapshot));
		return checks;
	}

	/// <summary>The listening sockets anyone who can reach the machine can connect to: bound to every address, not loopback.</summary>
	public static IEnumerable<ListeningSocket> OpenToAll(IEnumerable<ListeningSocket> sockets) =>
		(sockets ?? throw new ArgumentNullException(nameof(sockets))).Where(socket => socket.Address is "0.0.0.0" or "::" or "*" or "[::]");

	/// <summary>The value sshd uses for <paramref name="keyword"/>: its own word, else the files', else its default.</summary>
	public static string ValueOf(SshdConfig config, string keyword)
	{
		ArgumentNullException.ThrowIfNull(config);
		string key = keyword.ToLowerInvariant();
		string? value = config.Get(key);

		// OpenSSH 8.7 renamed ChallengeResponseAuthentication; older servers and older files still say the old name.
		if (value is null && key == "kbdinteractiveauthentication")
		{
			value = config.Get("challengeresponseauthentication");
		}

		return value ?? Defaults.GetValueOrDefault(key, "");
	}

	private static IEnumerable<SecurityCheck> Sshd(SshdConfig config, SessionLogin login)
	{
		if (!config.Installed && config.Files.Count == 0)
		{
			yield return new SecurityCheck
			{
				Id = "sshd",
				Title = "SSH server",
				Level = SecurityLevel.Unknown,
				Summary = config.Unreadable.Count > 0
					? "Its settings are root's alone here. Turn on root mode to read them."
					: "There is no OpenSSH sshd here, so its settings are not read. Another server (dropbear) may be answering.",
			};
			yield break;
		}

		bool offerFixes = config.Effective && config.KnowsFiles;
		string password = Lower(ValueOf(config, "PasswordAuthentication"));
		yield return Check(config, "PasswordAuthentication", "Password logins", password,
			password == "yes" ? SecurityLevel.Warning : SecurityLevel.Good,
			password == "yes"
				? "Anyone can try passwords, and a password can be guessed. Keys cannot."
				: "Only keys and the other methods log in; a password typed at the prompt is refused.",
			password == "yes" && offerFixes ? TurnOff("PasswordAuthentication", "Turn password logins off", login.MayUsePassword ? PasswordWarning(login) : null) : null);

		string keyboard = Lower(ValueOf(config, "KbdInteractiveAuthentication"));
		bool pam = Lower(ValueOf(config, "UsePAM")) == "yes";
		string keyboardKeyword = config.Values.ContainsKey("kbdinteractiveauthentication") || !config.Values.ContainsKey("challengeresponseauthentication")
			? "KbdInteractiveAuthentication"
			: "ChallengeResponseAuthentication";
		yield return Check(config, keyboardKeyword, "Keyboard-interactive logins", keyboard,
			keyboard != "yes" ? SecurityLevel.Good : pam ? SecurityLevel.Warning : SecurityLevel.Notice,
			keyboard != "yes"
				? "Off: sshd asks no questions of its own, such as a password through PAM."
				: pam
					? "On with PAM, which is another way to log in with a password, even with password logins off."
					: "On: sshd may ask questions of its own, such as a one-time code.",
			keyboard == "yes" && offerFixes
				? TurnOff(keyboardKeyword, "Turn keyboard-interactive off", login.MayUsePassword ? KeyboardWarning(login) : null)
				: null);

		string root = Lower(ValueOf(config, "PermitRootLogin"));
		bool rootByKey = root is "prohibit-password" or "without-password" or "forced-commands-only";
		yield return Check(config, "PermitRootLogin", "Root logins", root,
			root == "yes" ? SecurityLevel.Warning : SecurityLevel.Good,
			root switch
			{
				"yes" => "root may log in with a password, the account every scan tries first.",
				"no" => "root cannot log in over SSH; administrators log in as themselves and use sudo.",
				"forced-commands-only" => "root may only log in with a key that runs a fixed command.",
				_ => "root may log in with a key, never with a password.",
			},
			!offerFixes ? null
				: root == "yes" ? RootByKeyOnly(login)
				: rootByKey ? RootOff(login)
				: null);

		string pubkey = Lower(ValueOf(config, "PubkeyAuthentication"));
		yield return Check(config, "PubkeyAuthentication", "Key logins", pubkey,
			pubkey == "no" ? SecurityLevel.Warning : SecurityLevel.Good,
			pubkey == "no" ? "Keys are refused, which leaves passwords, the weaker way in." : "Keys log in, the strongest way sshd offers.",
			pubkey == "no" && offerFixes ? new SshdFix { Keyword = "PubkeyAuthentication", Value = "yes", Accepts = ["yes"], Label = "Turn key logins on" } : null);

		string x11 = Lower(ValueOf(config, "X11Forwarding"));
		yield return Check(config, "X11Forwarding", "X11 forwarding", x11,
			x11 == "yes" ? SecurityLevel.Notice : SecurityLevel.Good,
			x11 == "yes" ? "Graphical programs can be shown on the client, which servers rarely need and which widens what a login can reach." : "Off, as servers usually want it.",
			x11 == "yes" && offerFixes ? TurnOff("X11Forwarding", "Turn X11 forwarding off", null) : null);

		string tries = ValueOf(config, "MaxAuthTries");
		bool many = int.TryParse(tries, NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count > MaxAuthTriesLimit;
		yield return Check(config, "MaxAuthTries", "Attempts per connection", tries,
			many ? SecurityLevel.Notice : SecurityLevel.Good,
			many
				? string.Create(CultureInfo.InvariantCulture, $"A connection may guess {count} times before sshd drops it; {MaxAuthTriesLimit} is plenty for people.")
				: "Few guesses per connection.",
			many && offerFixes
				? new SshdFix
				{
					Keyword = "MaxAuthTries",
					Value = MaxAuthTriesLimit.ToString(CultureInfo.InvariantCulture),
					Accepts = [MaxAuthTriesLimit.ToString(CultureInfo.InvariantCulture)],
					Label = string.Create(CultureInfo.InvariantCulture, $"Set it to {MaxAuthTriesLimit}"),
					Warning = login.Method is null or AuthenticationMethod.Agent
						? "A client that offers more keys than this before the right one is refused; an SSH agent holding many keys can be."
						: null,
				}
				: null);

		string users = ValueOf(config, "AllowUsers");
		string groups = ValueOf(config, "AllowGroups");
		bool limited = users.Length > 0 || groups.Length > 0;
		yield return new SecurityCheck
		{
			Id = "sshd.allow",
			Title = "Who may log in",
			Keyword = "AllowUsers",
			Value = limited ? string.Join(" · ", new[] { users.Length > 0 ? $"users {users}" : "", groups.Length > 0 ? $"groups {groups}" : "" }.Where(part => part.Length > 0)) : "everyone",
			Level = limited ? SecurityLevel.Good : SecurityLevel.Notice,
			Summary = limited
				? "Only the accounts AllowUsers and AllowGroups name may log in over SSH."
				: "Every account with a login shell may log in over SSH. AllowUsers or AllowGroups would limit it to the ones that need to.",
		};
	}

	private static SecurityCheck Check(SshdConfig config, string keyword, string title, string value, SecurityLevel level, string summary, SshdFix? fix)
	{
		// Without sshd -T and without the main file, the value would only be sshd's default, which says nothing.
		bool known = config.Effective || config.KnowsFiles;
		return new SecurityCheck
		{
			Id = "sshd." + keyword.ToLowerInvariant(),
			Title = title,
			Keyword = keyword,
			Value = known && value.Length > 0 ? value : "?",
			Level = known ? level : SecurityLevel.Unknown,
			Summary = known ? summary : "The SSH server's files are root's alone here. Turn on root mode to read them.",
			Fix = fix,
		};
	}

	private static SshdFix TurnOff(string keyword, string label, string? warning) => new()
	{
		Keyword = keyword,
		Value = "no",
		Accepts = ["no"],
		Label = label,
		Warning = warning,
		CutsConnection = warning is not null,
	};

	private static SshdFix RootByKeyOnly(SessionLogin login)
	{
		bool cuts = login.IsRoot && login.MayUsePassword;
		return new SshdFix
		{
			Keyword = "PermitRootLogin",
			Value = "prohibit-password",
			Accepts = ["prohibit-password", "without-password"],
			Label = "Let root in with keys only",
			CutsConnection = cuts,
			Warning = cuts
				? login.Method is null
					? "This session logged in as root, and this view cannot tell how. If it typed a password, the next root login with it is refused, this app's included."
					: "This session logged in as root with a password. The next root login with a password is refused, this app's included; this session stays open."
				: null,
		};
	}

	private static SshdFix RootOff(SessionLogin login) => new()
	{
		Keyword = "PermitRootLogin",
		Value = "no",
		Accepts = ["no"],
		Label = "Turn root logins off",
		CutsConnection = login.IsRoot,
		Warning = login.IsRoot
			? "This session logged in as root. Turning root logins off refuses the next one, this app's included; this session stays open. Log in as another account with sudo first."
			: null,
	};

	private static string PasswordWarning(SessionLogin login) => login.Method is null
		? "This view cannot tell how this session logged in. If it typed a password, the next login with one is refused, this app's included."
		: "This session logged in with a password. Turning password logins off refuses the next login the same way, this app's included; this session stays open. Add a key for the account first (Users, SSH keys).";

	private static string KeyboardWarning(SessionLogin login) => login.Method is null
		? "This view cannot tell how this session logged in. A password typed at sshd's prompts goes through keyboard-interactive, so the next login like that is refused."
		: "This session logged in with a password, which sshd may have taken through keyboard-interactive. The next login like that is refused, this app's included; this session stays open.";

	private static SecurityCheck Updates(int? count, string? note) => new()
	{
		Id = "updates.security",
		Title = "Security updates",
		Value = count is { } pending ? pending.ToString(CultureInfo.InvariantCulture) : "?",
		Level = count switch
		{
			null => SecurityLevel.Unknown,
			0 => SecurityLevel.Good,
			_ => SecurityLevel.Warning,
		},
		Summary = count switch
		{
			null => note ?? "Asking the package manager.",
			0 => "None waiting, as far as the package manager's lists know.",
			1 => "One update that fixes a security problem is waiting.",
			_ => string.Create(CultureInfo.InvariantCulture, $"{count} updates that fix security problems are waiting."),
		},
		Shows = "security-updates",
	};

	private static SecurityCheck Fail2Ban(Fail2BanStatus? status) => status switch
	{
		null => new SecurityCheck
		{
			Id = "fail2ban",
			Title = "fail2ban",
			Value = "not installed",
			Level = SecurityLevel.Notice,
			Summary = "Nothing bans an address that keeps guessing. fail2ban does, from the Packages page.",
			Shows = "packages",
		},
		{ Problem: { } problem } => new SecurityCheck
		{
			Id = "fail2ban",
			Title = "fail2ban",
			Value = "installed",
			Level = SecurityLevel.Unknown,
			Summary = problem,
		},
		_ => new SecurityCheck
		{
			Id = "fail2ban",
			Title = "fail2ban",
			Value = string.Create(CultureInfo.InvariantCulture, $"{status.Jails.Count} {(status.Jails.Count == 1 ? "jail" : "jails")}, {status.Jails.Sum(jail => jail.CurrentlyBanned ?? 0)} banned"),
			Level = status.Jails.Count > 0 ? SecurityLevel.Good : SecurityLevel.Notice,
			Summary = status.Jails.Count > 0 ? "Addresses that keep failing are banned for a while." : "It runs, but no jail is on, so it bans nobody.",
			Shows = "fail2ban",
		},
	};

	private static SecurityCheck Logins(SecuritySnapshot snapshot)
	{
		int addresses = snapshot.FailedLogins.Select(login => login.Address).Distinct(StringComparer.Ordinal).Count();
		return new SecurityCheck
		{
			Id = "logins",
			Title = "Failed logins",
			Value = snapshot.FailedLoginSource.Length == 0 ? "?" : snapshot.FailedLogins.Count.ToString(CultureInfo.InvariantCulture),
			Level = snapshot.FailedLoginSource.Length == 0 ? SecurityLevel.Unknown : snapshot.FailedLogins.Count > 0 ? SecurityLevel.Notice : SecurityLevel.Good,
			Summary = snapshot.FailedLoginSource.Length == 0
				? snapshot.AsRoot ? "No log of SSH logins was found." : "The login log is root's. Turn on root mode to read it."
				: snapshot.FailedLogins.Count == 0
					? "None in the recent log."
					: string.Create(CultureInfo.InvariantCulture, $"{snapshot.FailedLogins.Count} recent failures from {addresses} addresses, read from {snapshot.FailedLoginSource}."),
			Shows = "logins",
		};
	}

	private static SecurityCheck Ports(SecuritySnapshot snapshot)
	{
		int open = OpenToAll(snapshot.Listening).Select(socket => (socket.IsUdp, socket.Port)).Distinct().Count();
		return new SecurityCheck
		{
			Id = "ports",
			Title = "Open ports",
			Value = snapshot.ListeningSource.Length == 0 ? "?" : open.ToString(CultureInfo.InvariantCulture),
			Level = snapshot.ListeningSource.Length == 0 ? SecurityLevel.Unknown : open > 1 ? SecurityLevel.Notice : SecurityLevel.Good,
			Summary = snapshot.ListeningSource.Length == 0
				? "Neither ss nor netstat is installed."
				: string.Create(CultureInfo.InvariantCulture, $"{open} ports listen on every address, so anyone who can reach the machine can connect; {snapshot.Listening.Count} listen in all."),
			Shows = "ports",
		};
	}

	private static string Lower(string value) => value.ToLowerInvariant();
}
