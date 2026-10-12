using System.Globalization;
using Mokaterm.Modules.Ssh.Admin.Network;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Admin.Security;

/// <summary>
/// The security page's one read: sshd's settings (<c>sshd -T</c> when root, and the files either way), recent failed
/// SSH logins (the journal, else auth.log or secure, else <c>lastb</c>), fail2ban's jails and the listening ports.
/// Without root most of it comes back thinner rather than failing: sshd -T, other users' log lines, fail2ban and
/// lastb all need it.
/// </summary>
internal static class SecurityCommands
{
	/// <summary>The most failed login lines kept. Enough to see who is knocking; the log itself is the place for more.</summary>
	public const int MaxFailedLogins = 300;

	/// <summary>The journal lines looked through for them, newest first.</summary>
	public const int JournalLines = 5000;

	private const string FailurePattern = "Failed [a-z/-]+ for|Invalid user";

	public static AdminScript Read(bool asRoot) => new()
	{
		Name = "security.read",
		AsRoot = asRoot,
		Body = string.Join('\n',
			AdminSections.Start("uid"),
			"id -u",
			"if command -v sshd >/dev/null 2>&1; then",
			$"\t{AdminSections.Start("sshd")}",
			"\tcommand -v sshd",
			"\tif [ \"$(id -u)\" = 0 ]; then",
			$"\t\t{AdminSections.Start("sshd-t")}",
			"\t\tsshd -T 2>&1",
			"\t\tcode=$?",
			$"\t\t{AdminSections.Start("sshd-t-exit")}",
			"\t\techo \"$code\"",
			"\tfi",
			"fi",
			$"for f in {SshdConfig.MainFile} {SshdConfig.DropInFolder}/*.conf; do",
			"\t[ -f \"$f\" ] || continue",
			"\tif [ -r \"$f\" ]; then",
			"\t\tprintf '@@mt:sshd-file %s\\n' \"$f\"",
			"\t\tcat -- \"$f\"",
			"\t\techo",
			"\telse",
			"\t\tprintf '@@mt:sshd-unreadable %s\\n' \"$f\"",
			"\tfi",
			"done",
			"if [ -d /run/systemd/system ] && command -v journalctl >/dev/null 2>&1; then",
			$"\t{AdminSections.Start("logins-source")}",
			"\techo journal",
			$"\t{AdminSections.Start("logins")}",

			// sshd-session, not sshd, writes the login lines since OpenSSH 9.8.
			string.Create(CultureInfo.InvariantCulture, $"\tjournalctl --no-pager -o short-iso -n {JournalLines} SYSLOG_IDENTIFIER=sshd SYSLOG_IDENTIFIER=sshd-session 2>/dev/null | grep -E '{FailurePattern}' | tail -n {MaxFailedLogins}"),
			"else",
			"\tfor f in /var/log/auth.log /var/log/secure; do",
			"\t\tif [ -r \"$f\" ]; then",
			$"\t\t\t{AdminSections.Start("logins-source")}",
			"\t\t\tprintf '%s\\n' \"$f\"",
			$"\t\t\t{AdminSections.Start("logins")}",
			string.Create(CultureInfo.InvariantCulture, $"\t\t\tgrep -E 'sshd(-session)?\\[' \"$f\" | grep -E '{FailurePattern}' | tail -n {MaxFailedLogins}"),
			"\t\t\tbreak",
			"\t\tfi",
			"\tdone",
			"fi",
			"if command -v lastb >/dev/null 2>&1 && [ -r /var/log/btmp ]; then",
			$"\t{AdminSections.Start("lastb")}",
			"\tlastb -w --time-format iso -n 100 2>/dev/null",
			"fi",
			"if command -v fail2ban-client >/dev/null 2>&1; then",
			$"\t{AdminSections.Start("fail2ban")}",
			"\tfail2ban-client status 2>&1",
			"\tfor jail in $(fail2ban-client status 2>/dev/null | sed -n 's/.*Jail list:[[:space:]]*//p' | tr ',' ' '); do",
			"\t\tprintf '@@mt:jail %s\\n' \"$jail\"",
			"\t\tfail2ban-client status \"$jail\" 2>&1",
			"\tdone",
			"fi",
			NetworkCommands.ListeningPorts,
			"exit 0"),
	};

	public static SecuritySnapshot Parse(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		bool asRoot = AdminSections.Lines(sections.Get("uid")).FirstOrDefault()?.Trim() == "0";
		(IReadOnlyList<ListeningSocket> listening, string listeningSource) = NetworkParser.ParseListening(sections);
		IReadOnlyList<FailedLogin> logins = FailedLoginParser.ParseLog(sections.Get("logins"));
		string loginSource = AdminSections.Lines(sections.Get("logins-source")).FirstOrDefault()?.Trim() ?? "";
		if (logins.Count == 0 && sections.Get("lastb") is { } lastb)
		{
			logins = FailedLoginParser.ParseLastb(lastb);
			loginSource = logins.Count > 0 ? "lastb" : loginSource;
		}

		return new SecuritySnapshot
		{
			AsRoot = asRoot,
			Sshd = ParseSshd(sections),
			FailedLogins = logins,
			FailedLoginSource = loginSource,
			Fail2Ban = sections.Has("fail2ban") ? Fail2BanStatus.Parse(sections.Get("fail2ban"), sections.All("jail")) : null,
			Listening = listening,
			ListeningSource = listeningSource,
		};
	}

	private static SshdConfig ParseSshd(AdminSections sections)
	{
		Dictionary<string, string> files = new(StringComparer.Ordinal);
		foreach ((string path, string text) in sections.All("sshd-file"))
		{
			files.TryAdd(path, text);
		}

		List<string> unreadable = [.. sections.All("sshd-unreadable").Select(part => part.Value)];
		bool installed = sections.Has("sshd");
		if (!installed && files.Count == 0)
		{
			return SshdConfig.None;
		}

		SshdConfig fromFiles = SshdConfigParser.ParseFiles(files, unreadable) with { Installed = installed };
		if (sections.Get("sshd-t") is not { } test)
		{
			return fromFiles;
		}

		bool passed = AdminSections.Lines(sections.Get("sshd-t-exit")).FirstOrDefault()?.Trim() == "0";
		Dictionary<string, string> effective = SshdConfigParser.ParseEffective(test);
		return passed && effective.Count > 0
			? fromFiles with { Values = effective, Effective = true }
			: fromFiles with { TestProblem = RemoteText.FirstLine(test) is { Length: > 0 } line ? line : "sshd -T printed nothing." };
	}
}
