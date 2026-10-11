using System.Diagnostics;
using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Accounts;
using Mokaterm.Modules.Ssh.Admin.Clock;
using Mokaterm.Modules.Ssh.Admin.Cron;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Admin.Firewall;
using Mokaterm.Modules.Ssh.Admin.SystemLogs;
using Mokaterm.Modules.Ssh.Admin.Naming;
using Mokaterm.Modules.Ssh.Admin.Network;
using Mokaterm.Modules.Ssh.Admin.Overview;
using Mokaterm.Modules.Ssh.Admin.Packages;
using Mokaterm.Modules.Ssh.Admin.Processes;
using Mokaterm.Modules.Ssh.Admin.Services;
using Mokaterm.Modules.Ssh.Admin.Storage;
using Mokaterm.Modules.Ssh.Elevation;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>
/// What goes around a script on the wire, checked as text and, where a POSIX sh exists here, by running it: every
/// script the view can send must parse, and the stdin framing must hand the command exactly its payload whether or
/// not sudo took a password line first.
/// </summary>
public sealed class AdminScriptTests
{
	private static readonly TimeSpan ShellTimeout = TimeSpan.FromSeconds(30);

	public static TheoryData<int, string> AllScripts()
	{
		TheoryData<int, string> data = [];
		int index = 0;
		foreach (AdminScript script in Scripts())
		{
			data.Add(index++, script.Name);
		}

		return data;
	}

	[Fact]
	public void Compose_PutsTheTagAndPreambleFirst_InUnixLineEndings()
	{
		string text = AdminScriptText.Compose(new AdminScript { Name = "services.list", Body = "echo one\r\necho two" });

		Assert.StartsWith("# mokaterm system view: services.list\nLC_ALL=C\nexport LC_ALL\n", text, StringComparison.Ordinal);
		Assert.Contains("printf 'MOKATERM-OUT\\0'", text, StringComparison.Ordinal);
		Assert.EndsWith("echo one\necho two\n", text, StringComparison.Ordinal);
		Assert.DoesNotContain('\r', text);
		Assert.DoesNotContain("MOKATERM-STDIN", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Compose_ReadsUpToTheMarker_OnlyWhenTheScriptTakesInput()
	{
		string text = AdminScriptText.Compose(new AdminScript { Name = "cron.write", Body = "crontab -", ReadsInput = true });

		Assert.Contains("MOKATERM-STDIN", text, StringComparison.Ordinal);
		Assert.True(text.IndexOf("MOKATERM-STDIN", StringComparison.Ordinal) < text.IndexOf("crontab -", StringComparison.Ordinal));
	}

	[Fact]
	public void FrameInput_PutsTheMarkerLineFirst_AndDropsOneFinalBreak()
	{
		using AdminInput input = AdminInput.Visible("0 3 * * * a\r\n0 4 * * * b\n");
		using SecretBuffer framed = AdminScriptText.FrameInput(input);

		Assert.Equal("MOKATERM-STDIN\n0 3 * * * a\n0 4 * * * b", framed.RevealString());
		Assert.Equal("0 3 * * * a\n0 4 * * * b\n", input.Preview);
	}

	[Fact]
	public void Secrets_ShowOnlyTheirPreview()
	{
		using AdminInput input = AdminInput.Secret("alice:hunter2", "alice:********");

		Assert.Equal("alice:********", input.Preview);
		Assert.DoesNotContain("hunter2", input.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain("echo", new AdminScript { Name = "x", Body = "echo secret" }.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void ToOutcome_StartsAtTheMarker_AndCutsAPartialLine()
	{
		byte[] stdout = Encoding.UTF8.GetBytes("Welcome to the server\nMOKATERM-OUT\0line one\nline two\nhalf a li");
		RemoteCommandOutcome raw = new()
		{
			ExitCode = 0,
			Output = new CapturedOutput(stdout, stdout.Length + 100),
			Error = CapturedOutput.Empty,
		};

		AdminOutcome outcome = AdminScriptText.ToOutcome(raw, "");

		Assert.Equal("line one\nline two\n", outcome.Output);
		Assert.True(outcome.IsTruncated);
	}

	[Fact]
	public void ToOutcome_WithoutTheMarker_SaysTheScriptNeverRan()
	{
		byte[] stdout = Encoding.UTF8.GetBytes("This account is currently not available.\n");
		RemoteCommandOutcome raw = new()
		{
			ExitCode = 1,
			Output = new CapturedOutput(stdout, stdout.Length),
			Error = CapturedOutput.Empty,
		};

		AdminException refused = Assert.Throws<AdminException>(() => AdminScriptText.ToOutcome(raw, "rbash: sh: restricted\n"));
		Assert.Contains("rbash: sh: restricted", refused.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Problem_IsTheFirstErrorLine_OrTheExitCode()
	{
		Assert.Equal("E: Unable to locate package nope", new AdminOutcome { ExitCode = 100, Error = "\nE: Unable to locate package nope\nmore\n" }.Problem());
		Assert.Equal("The command ended with exit code 3.", new AdminOutcome { ExitCode = 3 }.Problem());
		Assert.Equal("The command was ended by KILL.", new AdminOutcome { ExitSignal = "KILL" }.Problem());
	}

	[Fact]
	public void SudoCommand_KeepsTheScriptInOneQuotedWord()
	{
		string script = AdminScriptText.Compose(new AdminScript { Name = "services.restart", Body = "systemctl restart -- 'it'\\''s.service'" });
		string command = SudoRunner.BuildCommand("sudo", withPassword: true, script, []);

		Assert.StartsWith("LC_ALL=C 'sudo' -S -p '' -- sh -c '", command, StringComparison.Ordinal);
		Assert.EndsWith("' sh", command, StringComparison.Ordinal);
		Assert.DoesNotContain('\r', command);
	}

	[Theory]
	[MemberData(nameof(AllScripts))]
	public async Task EveryScript_ParsesInAPosixShell(int index, string name)
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		AdminScript script = Scripts().ElementAt(index);
		Assert.Equal(name, script.Name);
		string text = AdminScriptText.Compose(script);
		(int exitCode, _, string error) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "sh -n <<'MOKATERM_SCRIPT'\n" + text + "\nMOKATERM_SCRIPT\n", []).WaitAsync(ShellTimeout, TestContext.Current.CancellationToken);

		Assert.True(exitCode == 0, $"{script.Name}: {error}");
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task InputFraming_HandsTheCommandOnlyItsPayload(bool sudoLeftThePasswordLine)
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		string script = AdminScriptText.Compose(new AdminScript { Name = "test.input", Body = "cat", ReadsInput = true });
		using AdminInput input = AdminInput.Visible("first line\nsecond: with 'quotes' and $dollars\n");
		using SecretBuffer framed = AdminScriptText.FrameInput(input);

		// When sudo did not need its password it never reads that line, and the script must skip it.
		string stdin = (sudoLeftThePasswordLine ? "the sudo password\n" : "") + framed.RevealString() + "\n";
		(int exitCode, byte[] output, string error) = await RunWithStdinAsync(shell, script, stdin);

		Assert.True(exitCode == 0, error);
		Assert.True(RemoteOutput.TryGetPayload(output, out ReadOnlyMemory<byte> payload));
		Assert.Equal("first line\nsecond: with 'quotes' and $dollars\n", Encoding.UTF8.GetString(payload.Span));
	}

	[Fact]
	public async Task InputFraming_RefusesWhenThePayloadNeverArrived()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		string script = AdminScriptText.Compose(new AdminScript { Name = "test.input", Body = "echo ran", ReadsInput = true });
		(int exitCode, byte[] output, string error) = await RunWithStdinAsync(shell, script, "the sudo password\n");

		Assert.Equal(1, exitCode);
		Assert.Contains("did not arrive", error, StringComparison.Ordinal);
		Assert.DoesNotContain("ran", Encoding.UTF8.GetString(output), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Detection_RunsInALocalShell_AndParses()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		(int exitCode, byte[] output, string error) = await RunWithStdinAsync(shell, PosixShellLocator.UnixPathPrefix + AdminScriptText.Compose(SystemProbe.Script), "");

		Assert.True(exitCode == 0, error);
		Assert.True(RemoteOutput.TryGetPayload(output, out ReadOnlyMemory<byte> payload));
		SystemFacts facts = SystemProbe.Parse(Encoding.UTF8.GetString(payload.Span));
		Assert.NotEmpty(facts.KernelName);
		Assert.NotNull(facts.Uid);
		Assert.Contains("ps", facts.Commands);
	}

	/// <summary>Every script the view can build, for the syntax check.</summary>
	private static IEnumerable<AdminScript> Scripts()
	{
		SystemFacts facts = SystemProbe.Parse(DetectionFixtures.Debian12);
		yield return SystemProbe.Script;
		yield return OverviewCommands.Read();
		yield return DiskCommands.Read();
		yield return NetworkCommands.Read(asRoot: false);
		yield return ProcessCommands.List(asRoot: false);
		yield return AccountCommands.Read(asRoot: true);
		yield return LogCommands.Journal("nginx.service", "err", 100, asRoot: false);
		yield return LogCommands.ListFiles(asRoot: false);
		yield return LogCommands.ReadFile("/var/log/syslog", 100, asRoot: false);
		yield return CronCommands.Read(asRoot: false);
		yield return CronCommands.ReadSystem(asRoot: false);
		yield return CronCommands.ListTimers();
		yield return CronCommands.Write(CrontabDocument.Parse("0 3 * * * x\n"), "", asRoot: false, "alice", "t").Action.Script;
		foreach (bool timedatectl in (bool[])[true, false])
		{
			ClockCommands clock = new(timedatectl);
			yield return clock.Read();
			yield return clock.ListZones();
			yield return clock.SetTimeZone("Europe/Berlin").Script;
			HostNameCommands names = new(timedatectl);
			yield return names.Read();
			yield return names.Set("web-02", null).Script;
		}

		foreach (InitSystem init in (InitSystem[])[InitSystem.Systemd, InitSystem.OpenRc, InitSystem.SysV, InitSystem.BusyBox])
		{
			ServiceCommands services = new(init, facts with { Commands = new HashSet<string>(["service", "update-rc.d"], StringComparer.Ordinal) });
			yield return services.List();
			yield return services.Status("cron", asRoot: false);
			foreach (ServiceAction action in Enum.GetValues<ServiceAction>())
			{
				if (services.CanEnable || action is not (ServiceAction.Enable or ServiceAction.Disable))
				{
					yield return services.Act(action, "cron").Script;
				}
			}
		}

		foreach (PackageManagerKind kind in (PackageManagerKind[])[PackageManagerKind.Apt, PackageManagerKind.Dnf, PackageManagerKind.Yum, PackageManagerKind.Zypper, PackageManagerKind.Pacman, PackageManagerKind.Apk])
		{
			PackageCommands packages = new(kind);
			yield return packages.ListInstalled();
			yield return packages.ListUpdates();
			yield return packages.UpgradeAll().Script;
			yield return packages.Install("htop").Script;
			yield return packages.Remove("htop").Script;
			if (packages.RefreshIndex() is { } refresh)
			{
				yield return refresh.Script;
			}
		}

		foreach (FirewallKind kind in (FirewallKind[])[FirewallKind.Ufw, FirewallKind.Firewalld, FirewallKind.Nftables, FirewallKind.Iptables])
		{
			FirewallCommands firewall = new(kind, 22);
			yield return firewall.Status();
			if (firewall.CanChange)
			{
				yield return firewall.Enable(null).Script;
				yield return firewall.Disable().Script;
				yield return firewall.Allow(new FirewallPort(53, "")).Script;
				yield return firewall.Deny(new FirewallPort(8080, "tcp")).Script;
			}
		}

		foreach (AccountToolKind tools in (AccountToolKind[])[AccountToolKind.Shadow, AccountToolKind.BusyBox])
		{
			AccountCommands accounts = new(tools, hasChpasswd: true);
			yield return accounts.AddUser("alice", "Alice", "/bin/sh", true, "pw").Action.Script;
			yield return accounts.SetPassword("alice", "pw").Action.Script;
			yield return accounts.Lock("alice", true).Script;
			yield return accounts.AddToGroup("alice", "wheel").Script;
			yield return accounts.DeleteUser(new UserAccount { Name = "bob", Uid = 1001 }, true, "alice").Script;
		}

		NetworkSnapshot snapshot = NetworkParser.Parse(NetworkFixtures.IpJson);
		yield return NetworkCommands.SetLink(snapshot.Interfaces[1], up: false, snapshot, null).Script;
		yield return ProcessCommands.Signal(new ProcessInfo { Pid = 4242, Command = "x" }, force: true, asRoot: true).Script;
	}

	private static async Task<(int ExitCode, byte[] Output, string Error)> RunWithStdinAsync(string shell, string script, string stdin)
	{
		ProcessStartInfo start = new(shell)
		{
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		start.ArgumentList.Add("-c");
		start.ArgumentList.Add(script);
		start.ArgumentList.Add("sh");
		using Process process = Process.Start(start) ?? throw new InvalidOperationException("sh did not start.");
		using MemoryStream output = new();
		Task copy = process.StandardOutput.BaseStream.CopyToAsync(output);
		Task<string> error = process.StandardError.ReadToEndAsync();
		await process.StandardInput.BaseStream.WriteAsync(Encoding.UTF8.GetBytes(stdin));
		process.StandardInput.Close();

		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		timeout.CancelAfter(ShellTimeout);
		await copy.WaitAsync(timeout.Token);
		string errorText = await error.WaitAsync(timeout.Token);
		await process.WaitForExitAsync(timeout.Token);
		return (process.ExitCode, output.ToArray(), errorText);
	}
}
