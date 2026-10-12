using System.Globalization;
using System.Text;
using Mokaterm.DevHost.Demo.FileSystem;

namespace Mokaterm.DevHost.Demo;

internal static partial class DemoMonitorOutput
{
	/// <summary>The demo's processes: who runs them, their parents and nice values, and how busy each is.</summary>
	private static DemoProcess[] ProcessTable(DemoAccount account) =>
	[
		new(1, 0, "root", 0, 0.002, 0.001, 30, 0.1, 13212, "Ss", "/sbin/init"),
		new(2, 0, "root", 0, 0, 0, 1, 0, 0, "S", "[kthreadd]"),
		new(9921, 2, "root", 0, 0.001, 0.001, 7, 0, 0, "I", "[kworker/1:2-events]"),
		new(612, 1, "systemd-resolve", 0, 0.001, 0.001, 11, 0.1, 12900, "Ss", "/usr/lib/systemd/systemd-resolved"),
		new(801, 1, "root", 0, 0.001, 0.001, 13, 0.0, 9012, "Ss", "sshd: /usr/sbin/sshd -D [listener] 0 of 10-100 startups"),
		new(48190, 801, "root", 0, 0, 0, 1, 0.1, 11200, "Ss", "sshd: deploy [priv]"),
		new(48201, 48190, "deploy", 0, 0.004, 0.003, 5, 0.1, 7400, "S", "sshd: deploy@pts/0"),
		new(48202, 48201, "deploy", 0, 0, 0, 1, 0.1, 5300, "Ss+", "-bash"),
		new(48230, 801, "root", 0, 0, 0, 1, 0.1, 11100, "Ss", $"sshd: {account.Name} [priv]"),
		new(48241, 48230, account.Name, 0, 0.01, 0.008, 4, 0.1, 7300, "S", $"sshd: {account.Name}@notty"),
		new(1123, 1, "root", 0, 0.001, 0.001, 17, 0.0, 5512, "Ss", "nginx: master process /usr/sbin/nginx -g daemon on; master_process on;"),
		new(1124, 1123, "www-data", 0, 0.06, 0.05, 9, 0.3, 26112, "S", "nginx: worker process"),
		new(1125, 1123, "www-data", 0, 0.04, 0.035, 6, 0.3, 25980, "S", "nginx: worker process"),
		new(1310, 1, "postgres", 0, 0.01, 0.008, 23, 3.9, 318004, "Ss", "/usr/lib/postgresql/16/bin/postgres -D /var/lib/postgresql/16/main"),
		new(1311, 1310, "postgres", 0, 0.005, 0.004, 31, 0.6, 51200, "Ss", "postgres: 16/main: checkpointer"),
		new(1312, 1310, "postgres", 0, 0.02, 0.018, 3.3, 0.2, 18000, "Ss", "postgres: 16/main: walwriter"),
		new(4410, 1, "deploy", 5, 0.45, 0.38, 12, 6.1, 498112, "Rl", "node /srv/app/server.js"),
		new(4411, 4410, "deploy", 5, 0.12, 0.11, 4.5, 1.2, 98112, "Sl", "node /srv/app/worker.js --queue mail"),
		new(5120, 48241, account.Name, 0, 0.02, 0.015, 8, 0.4, 30210, "S", "python3 -m http.server 8000"),
	];

	/// <summary>What the process list script prints: the memory total, two samples a second apart, then ps.</summary>
	private static string Processes(DemoAccount account, DateTimeOffset now)
	{
		double t = Seconds(now);
		DemoProcess[] table = ProcessTable(account);
		StringBuilder text = new();
		text.Append("@@mt:memtotal\nMemTotal:        8037888 kB\n@@mt:cpus\n")
			.Append(Cores.ToString(CultureInfo.InvariantCulture)).Append('\n');
		foreach ((string section, double at) in (ReadOnlySpan<(string, double)>)[("before", t), ("after", t + 1)])
		{
			long total = (long)(at * 100 * Cores);
			text.Append("@@mt:").Append(section).Append('\n')
				.Append(CultureInfo.InvariantCulture, $"cpu  {total * 3 / 10} 0 {total / 10} {total - (total * 3 / 10) - (total / 10)} 0 0 0 0 0 0\n");
			foreach (DemoProcess process in table)
			{
				long ticks = (long)(100 * Integral(at, process.Busy, (process.Swing, process.Period)));
				text.Append(CultureInfo.InvariantCulture, $"{process.Pid} {process.Parent} {process.Nice} {ticks * 3 / 4} {ticks - (ticks * 3 / 4)}\n");
			}
		}

		text.Append("@@mt:procps\n");
		foreach (DemoProcess process in table)
		{
			text.Append(CultureInfo.InvariantCulture, $"{process.Pid,7} {process.User,-32} {process.Busy * 100 / 3:0.0} {process.Memory:0.0} {process.Rss,6} {process.State,-4} {process.Command}\n");
		}

		return text.ToString();
	}
}
