using System.Globalization;
using Mokaterm.DevHost.Demo.Terminal;

namespace Mokaterm.DevHost.Demo;

/// <summary>Plausible syslog lines, for the seeded log files and for <c>tail -f</c>.</summary>
internal static class DemoLog
{
	private const string UserPlaceholder = "{user}";

	private static readonly Entry[] Entries =
	[
		new("sshd", 1874, Level.Info, "Accepted publickey for {user} from 192.168.1.44 port 51522 ssh2: ED25519 SHA256:q2Ys0l9nF7kX3pVb8wZr1mT6cH4uJ5eA0dG2iL7oN8s"),
		new("nginx", 1123, Level.Info, "GET /api/sessions HTTP/1.1 200 1843 12ms"),
		new("nginx", 1123, Level.Info, "POST /api/transfers HTTP/1.1 201 96 48ms"),
		new("nginx", 1123, Level.Warn, "upstream response time 1.84s for GET /api/files/list"),
		new("kernel", 0, Level.Info, "[UFW BLOCK] IN=eth0 OUT= SRC=203.0.113.7 DST=10.10.2.3 PROTO=TCP SPT=51202 DPT=23"),
		new("systemd", 1, Level.Info, "Started session-412.scope, session 412 of user {user}."),
		new("CRON", 21870, Level.Info, "({user}) CMD (/usr/local/bin/backup.sh --incremental)"),
		new("dockerd", 2210, Level.Debug, "Calling HEAD /_ping"),
		new("mokaterm-agent", 2718, Level.Error, "transfer 7f3a1c failed: connection reset by peer"),
		new("postgres", 1187, Level.Warn, "checkpoints are occurring too frequently (24 seconds apart)"),
		new("sshd", 1874, Level.Warn, "Failed password for invalid user admin from 198.51.100.23 port 40022 ssh2"),
		new("systemd-resolved", 612, Level.Info, "Clock change detected. Flushing caches."),
		new("mokaterm-agent", 2718, Level.Info, "session 3c1d opened for {user} (xterm-256color 120x32)"),
		new("kernel", 0, Level.Error, "EXT4-fs warning (device sda1): ext4_dx_add_entry: directory index full"),
		new("dockerd", 2210, Level.Info, "Container 4be2c1 health status changed to healthy"),
		new("nginx", 1123, Level.Error, "connect() failed (111: Connection refused) while connecting to upstream"),
	];

	private enum Level
	{
		Debug,
		Info,
		Warn,
		Error,
	}

	/// <summary>One line; <paramref name="sequence"/> picks the event, so the same sequence gives the same line.</summary>
	public static string Line(ulong sequence, DateTimeOffset time, string hostName, string userName, bool color)
	{
		Entry entry = Entries[DemoRandom.Next(sequence, Entries.Length)];
		string stamp = DemoDates.Syslog(time);
		string process = entry.Pid == 0 ? entry.Process : string.Create(CultureInfo.InvariantCulture, $"{entry.Process}[{entry.Pid}]");
		string message = entry.Message.Replace(UserPlaceholder, userName, StringComparison.Ordinal);
		(string level, string levelColor) = entry.Level switch
		{
			Level.Debug => ("DEBUG", Ansi.Blue),
			Level.Info => ("INFO ", Ansi.Green),
			Level.Warn => ("WARN ", Ansi.Yellow),
			_ => ("ERROR", Ansi.BoldRed),
		};

		return color
			? $"{Ansi.Dim}{stamp}{Ansi.Reset} {hostName} {Ansi.Cyan}{process}{Ansi.Reset}: {levelColor}{level}{Ansi.Reset} {message}"
			: $"{stamp} {hostName} {process}: {level} {message}";
	}

	/// <summary><paramref name="count"/> lines a few seconds apart, the last one at <paramref name="end"/>.</summary>
	public static string Lines(int count, DateTimeOffset end, ulong seed, string hostName, string userName)
	{
		IEnumerable<string> lines = Enumerable.Range(0, count).Select(index =>
		{
			ulong sequence = seed + (ulong)index;
			TimeSpan before = TimeSpan.FromSeconds(((count - index) * 7) + DemoRandom.Next(sequence, 6));
			return Line(sequence, end - before, hostName, userName, color: false);
		});

		return string.Join('\n', lines) + "\n";
	}

	private sealed record Entry(string Process, int Pid, Level Level, string Message);
}
