using System.Text;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Monitoring;
using Mokaterm.Modules.Ssh.Admin.Processes;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>The process list's CPU sampled over a second, its tree by parent, and renice.</summary>
public sealed class ProcessSamplingTests
{
	private static readonly TimeSpan ShellTimeout = TimeSpan.FromSeconds(30);

	/// <summary>Four CPUs, 600 ticks between the two readings: 150 per CPU, so 150 ticks of one process is one whole core.</summary>
	private const string Procps = """
		@@mt:memtotal
		MemTotal:        8037888 kB
		@@mt:cpus
		4
		@@mt:before
		cpu  1000 0 500 8000 100 0 0 0 0 0
		1 0 0 10 20
		801 1 0 5 5
		2211 801 0 1000 200
		4242 1 -5 1500 300
		@@mt:after
		cpu  1250 0 600 8240 110 0 0 0 0 0
		1 0 0 10 20
		801 1 0 5 5
		2211 801 0 1090 260
		4242 1 -5 1530 300
		5000 2211 0 15 0
		@@mt:procps
		      1 root                              0.0  0.1 13212 Ss   /sbin/init
		    801 root                              0.0  0.0  9012 Ss   sshd: /usr/sbin/sshd -D [listener] 0 of 10-100 startups
		   2211 www-data                         12.5  3.2 262144 Sl   nginx: worker process
		   4242 deploy                            3.0  1.0  1000 R    node /srv/app/server.js
		   5000 www-data                          0.0  0.0   100 S    nginx: cache loader
		   6000 root                              7.7  0.0     0 S    late starter
		""";

	[Fact]
	public void Parse_MeasuresCpuOverTheSecond_AndJoinsParentAndNice()
	{
		IReadOnlyList<ProcessInfo> processes = ProcessCommands.Parse(Procps);

		ProcessInfo nginx = processes.Single(process => process.Pid == 2211);
		Assert.Equal(100, nginx.Cpu);
		Assert.True(nginx.CpuSampled);
		Assert.Equal(801, nginx.ParentPid);
		Assert.Equal(0, nginx.Nice);

		ProcessInfo node = processes.Single(process => process.Pid == 4242);
		Assert.Equal(20, node.Cpu);
		Assert.Equal(-5, node.Nice);

		// Started between the readings: everything it used counts.
		Assert.Equal(10, processes.Single(process => process.Pid == 5000).Cpu);

		// Started after them: ps's own figure, marked as such.
		ProcessInfo late = processes.Single(process => process.Pid == 6000);
		Assert.Equal(7.7, late.Cpu);
		Assert.False(late.CpuSampled);
		Assert.Null(late.ParentPid);
		Assert.Equal(0, processes.Single(process => process.Pid == 1).Cpu);
	}

	[Fact]
	public void Parse_WithoutAwk_ReadsWholeStatLines_AndBusyBoxPs()
	{
		const string Output = """
			@@mt:memtotal
			MemTotal:        1000000 kB
			@@mt:cpus
			1
			@@mt:before
			cpu  100 0 100 800 0 0 0 0 0 0
			1 (init) S 0 1 1 0 -1 4194560 1000 0 0 0 10 20 0 0 20 0 1 0 5 1000000 300
			42 (my (odd) name) R 1 42 42 0 -1 4194304 100 0 0 0 1500 300 0 0 39 19 3 0 900 9000000 3000
			@@mt:after
			cpu  150 0 100 850 0 0 0 0 0 0
			1 (init) S 0 1 1 0 -1 4194560 1000 0 0 0 10 20 0 0 20 0 1 0 5 1000000 300
			42 (my (odd) name) R 1 42 42 0 -1 4194304 100 0 0 0 1525 300 0 0 39 19 3 0 900 9000000 3000
			@@mt:procps
			@@mt:busybox
			PID   USER     RSS  STAT COMMAND
			    1 root     1024 S    /sbin/init
			   42 nobody    12m R    my (odd) name --flag
			""";

		IReadOnlyList<ProcessInfo> processes = ProcessCommands.Parse(Output);

		ProcessInfo odd = processes.Single(process => process.Pid == 42);
		Assert.Equal(25, odd.Cpu);
		Assert.Equal(1, odd.ParentPid);
		Assert.Equal(19, odd.Nice);
		Assert.Equal(1.2, odd.Memory);
		Assert.Equal(0, processes.Single(process => process.Pid == 1).ParentPid);
	}

	[Theory]
	[InlineData("1857 (bash) S 1 1857 1857 0 -1 0 3160 3160 0 0 0 62 0 62 20 0 0 0 1089194467 6578176 2635", 1857, 1, 0, 62)]
	[InlineData("7 (a) b) c) D 2 0 0 0 -1 0 0 0 0 0 4 5 0 0 0 -20 1 0", 7, 2, -20, 9)]
	[InlineData("1857 1 0 0 62", 1857, 1, 0, 62)]
	[InlineData("99 98 -3 100 1", 99, 98, -3, 101)]
	public void Ticks_FromAwkOrAWholeLine(string line, int pid, int parent, int nice, long ticks) =>
		Assert.Equal(new ProcessTicks(pid, parent, nice, ticks), ProcessTicks.Parse(line));

	[Theory]
	[InlineData("cpu  1 2 3 4")]
	[InlineData("1 2 3")]
	[InlineData("abc (x) S")]
	public void Ticks_RefuseOtherLines(string line) => Assert.Null(ProcessTicks.Parse(line));

	[Fact]
	public void CpuShares_WithoutTwoCpuLines_MeasureNothing()
	{
		Dictionary<int, ProcessTicks> ticks = new() { [1] = new ProcessTicks(1, 0, 0, 10) };

		Assert.Empty(ProcessCommands.CpuShares(null, ticks, new CpuTimes(1, 0, 0, 1, 0, 0, 0, 0), ticks, 1));
		Assert.Empty(ProcessCommands.CpuShares(new CpuTimes(1, 0, 0, 1, 0, 0, 0, 0), ticks, new CpuTimes(1, 0, 0, 1, 0, 0, 0, 0), ticks, 1));
	}

	[Fact]
	public void Tree_PutsChildrenUnderParents_InTheListsOrder()
	{
		IReadOnlyList<ProcessInfo> processes = ProcessCommands.Parse(Procps);

		IReadOnlyList<(ProcessInfo Process, int Depth)> tree = ProcessTree.Flatten(processes, level => level.OrderBy(process => process.Pid));

		Assert.Equal([(1, 0), (801, 1), (2211, 2), (5000, 3), (4242, 1), (6000, 0)], tree.Select(row => (row.Process.Pid, row.Depth)));
	}

	[Fact]
	public void Tree_ALoopOfParents_ShowsEveryProcessOnce()
	{
		ProcessInfo[] processes =
		[
			new() { Pid = 10, ParentPid = 11 },
			new() { Pid = 11, ParentPid = 10 },
			new() { Pid = 12, ParentPid = 12 },
			new() { Pid = 13, ParentPid = 11 },
		];

		IReadOnlyList<(ProcessInfo Process, int Depth)> tree = ProcessTree.Flatten(processes, level => level.OrderBy(process => process.Pid));

		Assert.Equal([12, 10, 11, 13], tree.Select(row => row.Process.Pid));
	}

	[Fact]
	public void Tree_Filtered_KeepsTheAncestorsOfAMatch()
	{
		IReadOnlyList<ProcessInfo> processes = ProcessCommands.Parse(Procps);

		(IReadOnlyList<ProcessInfo> shown, IReadOnlySet<int> context) = ProcessTree.WithAncestors(processes, process => process.Command.Contains("cache loader", StringComparison.Ordinal));

		Assert.Equal([1, 801, 2211, 5000], shown.Select(process => process.Pid).Order());
		Assert.Equal([1, 801, 2211], context.Order());
	}

	[Fact]
	public void Renice_SetsTheValueItself_AndNeedsRootToGoForward()
	{
		ProcessInfo node = new() { Pid = 4242, User = "deploy", Nice = 0, Command = "node /srv/app/server.js" };

		AdminAction action = ProcessCommands.Renice(node, -5, asRoot: true);

		Assert.Equal("renice -5 -p 4242", action.Script.Body);
		Assert.True(action.Script.AsRoot);
		Assert.Equal("processes.renice", action.Script.Name);
		Assert.NotNull(action.Warning);

		Assert.False(ProcessCommands.ReniceNeedsRoot(node, 10, "deploy"));
		Assert.True(ProcessCommands.ReniceNeedsRoot(node, -1, "deploy"));
		Assert.True(ProcessCommands.ReniceNeedsRoot(node, 10, "alice"));
		Assert.True(ProcessCommands.ReniceNeedsRoot(node with { Nice = null }, 10, "deploy"));
		Assert.Null(ProcessCommands.Renice(node, 19, asRoot: false).Warning);
		Assert.Throws<ArgumentOutOfRangeException>(() => ProcessCommands.Renice(node, 20, asRoot: false));
		Assert.Throws<ArgumentOutOfRangeException>(() => ProcessCommands.Renice(node, -21, asRoot: false));
	}

	[Fact]
	public async Task List_RunsInALocalShell_AndSamplesItsOwnProcesses()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		(int exitCode, byte[] output, string error) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + AdminScriptText.Compose(ProcessCommands.List(asRoot: false)), []).WaitAsync(ShellTimeout, TestContext.Current.CancellationToken);

		Assert.True(exitCode == 0, error);
		Assert.True(RemoteOutput.TryGetPayload(output, out ReadOnlyMemory<byte> payload));
		string text = Encoding.UTF8.GetString(payload.Span);
		AdminSections sections = AdminSections.Parse(text);
		(CpuTimes? before, IReadOnlyDictionary<int, ProcessTicks> first) = ProcessCommands.ParseTicks(sections.Get("before"));
		(CpuTimes? after, IReadOnlyDictionary<int, ProcessTicks> second) = ProcessCommands.ParseTicks(sections.Get("after"));

		// The shell running the script is in both readings, and the CPU line moved on by about a second.
		Assert.NotNull(before);
		Assert.NotNull(after);
		Assert.True(after.Value.Total > before.Value.Total);
		Assert.NotEmpty(first);
		Assert.Contains(second.Keys, first.ContainsKey);
		Assert.NotEmpty(ProcessCommands.Parse(text));
	}
}
