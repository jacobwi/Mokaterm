using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Fleet;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Admin.Overview;
using Mokaterm.Modules.Ssh.Admin.Packages;
using Mokaterm.Modules.Ssh.Admin.Survey;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>
/// The fleet overview's read of a machine: what the survey script is made of, what its parsers make of real output,
/// and a run in a local shell where there is one.
/// </summary>
public sealed class SurveyTests
{
	private static readonly TimeSpan ShellTimeout = TimeSpan.FromSeconds(60);

	[Fact]
	public void Ubuntu_ReadsEveryPart()
	{
		HostSummary summary = SurveyCommands.Parse(SurveyFixtures.Ubuntu());

		Assert.Equal("Ubuntu 24.04.1 LTS", summary.OperatingSystem);
		Assert.Equal("Linux 6.8.0-45-generic x86_64", summary.Kernel);
		Assert.Equal("app-01", summary.HostName);
		Assert.Equal(TimeSpan.FromSeconds(1036810), summary.Uptime);
		Assert.Equal(3.20, summary.Load1);
		Assert.Equal(1.75, summary.Load15);
		Assert.Equal(4, summary.CpuCount);
		Assert.Equal(80, Math.Round(summary.LoadPercent!.Value));
		Assert.Equal(8037888L * 1024, summary.MemoryTotalBytes);
		Assert.Equal((8037888L - 2009472) * 1024, summary.MemoryUsedBytes);
		Assert.Equal(75, Math.Round(summary.MemoryPercent!.Value));

		// The snap's loop image is full by design and is not storage; the data disk is the fullest real one.
		Assert.Equal("/srv/data", summary.FullestDisk?.MountPoint);
		Assert.Equal(92, summary.FullestDisk?.UsedPercent);
		Assert.Equal(209612800L * 1024, summary.FullestDisk?.TotalBytes);

		Assert.Equal(4, summary.PendingUpdates);
		Assert.Equal(2, summary.SecurityUpdates);
		Assert.True(summary.RebootRequired);
		Assert.Equal(2, summary.FailedServices);
		Assert.Equal(["backup-agent", "certbot"], summary.FailedServiceNames);
		Assert.Equal(2, summary.ContainersRunning);
		Assert.Equal(5, summary.ContainersTotal);
		Assert.Empty(summary.Notes);
	}

	[Theory]
	[InlineData("@@mt:needs-restarting\nReboot is required to fully utilize these updates.\n@@mt:needs-restarting-exit\n1\n", true)]
	[InlineData("@@mt:needs-restarting\nReboot should not be necessary.\n@@mt:needs-restarting-exit\n0\n", false)]
	[InlineData("@@mt:modules-gone\n", true)]
	[InlineData("@@mt:flag-watch\n", false)]
	[InlineData("", null)]
	public void Reboot_ReadsTheSystemViewsRebootCheck(string reboot, bool? expected) =>
		Assert.Equal(expected, SurveyCommands.Parse(SurveyFixtures.Ubuntu(reboot)).RebootRequired);

	[Fact]
	public void AlpineBusyBox_ReadsOpenRcApkAndBusyBoxDf()
	{
		HostSummary summary = SurveyCommands.Parse(SurveyFixtures.Survey(
			SurveyFixtures.Part("probe", DetectionFixtures.Alpine),
			SurveyFixtures.Part("overview", SurveyFixtures.AlpineOverview),
			SurveyFixtures.Part("updates", SurveyFixtures.AlpineUpdates),
			SurveyFixtures.Part("services", SurveyFixtures.AlpineServices),
			SurveyFixtures.Part("reboot", "@@mt:modules-gone\n"),
			SurveyFixtures.Part("docker", "")));

		Assert.Equal("Alpine Linux v3.20", summary.OperatingSystem);
		Assert.Equal(TimeSpan.FromSeconds(8100), summary.Uptime);
		Assert.Equal(948304L * 1024, summary.MemoryTotalBytes);

		// No MemAvailable: free plus buffers and cache, the way the overview works it out.
		Assert.Equal((948304L - 512000 - 20000 - 100000) * 1024, summary.MemoryUsedBytes);
		Assert.Equal("/", summary.FullestDisk?.MountPoint);
		Assert.Equal(25, summary.FullestDisk?.UsedPercent);
		Assert.Equal(2, summary.PendingUpdates);

		// apk has no security channel to count.
		Assert.Null(summary.SecurityUpdates);
		Assert.Equal(["nginx"], summary.FailedServiceNames);
		Assert.True(summary.RebootRequired);

		// Docker is not there at all, which is not the same as no containers.
		Assert.Null(summary.ContainersTotal);
		Assert.Null(summary.ContainersRunning);
	}

	[Fact]
	public void ACacheOnlyCheckThatFails_LeavesUpdatesUnknown_AndSaysWhy()
	{
		HostSummary summary = SurveyCommands.Parse(SurveyFixtures.Survey(
			SurveyFixtures.Part("probe", DetectionFixtures.Rocky9),
			SurveyFixtures.Part("updates", "@@mt:dnf\n", exitCode: 1),
			SurveyFixtures.Part("reboot", "@@mt:needs-restarting\nReboot should not be necessary.\n@@mt:needs-restarting-exit\n0\n")));

		Assert.Null(summary.PendingUpdates);
		Assert.Null(summary.SecurityUpdates);
		Assert.False(summary.RebootRequired);
		Assert.Equal(["The package manager could not list updates from its cache."], summary.Notes);
	}

	[Fact]
	public void APartThatRanOutOfTime_IsNoted_AndWhatItPrintedBeforeStillCounts()
	{
		HostSummary summary = SurveyCommands.Parse(SurveyFixtures.Survey(
			SurveyFixtures.Part("probe", DetectionFixtures.Ubuntu2404),
			SurveyFixtures.Part("overview", "@@mt:uptime\n600.5 1200.0\n@@mt:loadavg\n0.10 0.20 0.30 1/2 3\n@@mt:df\n", exitCode: 124),
			SurveyFixtures.Part("docker", "", exitCode: 124)));

		Assert.Equal(TimeSpan.FromSeconds(600), summary.Uptime);
		Assert.Null(summary.FullestDisk);
		Assert.Null(summary.ContainersTotal);
		Assert.Equal(["Load, memory and disks did not finish within 8 s.", "Docker did not finish within 8 s."], summary.Notes);
	}

	[Fact]
	public void AContainer_HasNoRebootOrServiceState()
	{
		HostSummary summary = SurveyCommands.Parse(SurveyFixtures.Survey(
			SurveyFixtures.Part("probe", DetectionFixtures.AlpineContainer),
			SurveyFixtures.Part("services", ""),
			SurveyFixtures.Part("reboot", "@@mt:modules-gone\n")));

		Assert.Null(summary.RebootRequired);
		Assert.Null(summary.FailedServices);
		Assert.Empty(summary.FailedServiceNames);
	}

	[Fact]
	public void ADaemonWithNoContainers_CountsZero()
	{
		HostSummary summary = SurveyCommands.Parse(SurveyFixtures.Survey(SurveyFixtures.Part("docker", "@@mt:status\n\n")));

		Assert.Equal(0, summary.ContainersRunning);
		Assert.Equal(0, summary.ContainersTotal);
	}

	[Fact]
	public void NothingRead_LeavesEveryFieldEmpty()
	{
		HostSummary summary = SurveyCommands.Parse("");

		Assert.Null(summary.OperatingSystem);
		Assert.Null(summary.Uptime);
		Assert.Null(summary.MemoryPercent);
		Assert.Null(summary.PendingUpdates);
		Assert.Null(summary.RebootRequired);
		Assert.Null(summary.FailedServices);
		Assert.Null(summary.ContainersTotal);
		Assert.Empty(summary.Notes);
	}

	[Fact]
	public void TheScript_RunsTheSystemViewsOwnScriptsWhole_AndReadsUpdatesOnlyFromTheCache()
	{
		string body = SurveyCommands.Script.Body;
		int start = body.IndexOf("mokaterm_part updates ", StringComparison.Ordinal);
		string updates = body[start..body.IndexOf("mokaterm_part services ", start, StringComparison.Ordinal)];

		Assert.Contains(PosixShell.Quote(SystemProbe.Script.Body), body, StringComparison.Ordinal);
		Assert.Contains(PosixShell.Quote(OverviewCommands.Read().Body), body, StringComparison.Ordinal);
		Assert.Contains("dnf -q -C check-update", updates, StringComparison.Ordinal);
		Assert.Contains("yum -q -C check-update", updates, StringComparison.Ordinal);
		Assert.Contains("--no-refresh", updates, StringComparison.Ordinal);
		Assert.Contains("pacman -Qu", updates, StringComparison.Ordinal);
		Assert.DoesNotContain("checkupdates", updates, StringComparison.Ordinal);
		Assert.DoesNotContain("apt-get update", updates, StringComparison.Ordinal);
		Assert.DoesNotContain("apk update", updates, StringComparison.Ordinal);
		Assert.DoesNotContain(" refresh", updates, StringComparison.Ordinal);
		Assert.DoesNotContain("makecache", updates, StringComparison.Ordinal);
		Assert.DoesNotContain("sudo -", body, StringComparison.Ordinal);
		Assert.True(SurveyCommands.Script.Timeout <= TimeSpan.FromMinutes(1));
		Assert.False(SurveyCommands.Script.AsRoot);
	}

	[Fact]
	public void PackageDetection_AndTheSurvey_TestTheSameCommands()
	{
		SystemFacts dnfAndYum = SystemProbe.Parse(DetectionFixtures.Rocky9) with
		{
			OsRelease = OsRelease.Empty,
			Commands = new HashSet<string>(["yum", "dnf", "rpm"], StringComparer.Ordinal),
		};

		// Without a distribution to go by, both pick the first manager in the same order: dnf before yum.
		Assert.Equal(PackageManagerKind.Dnf, SystemProfile.From(dnfAndYum).PackageManager);
		string body = SurveyCommands.Script.Body;
		Assert.True(
			body.IndexOf("command -v dnf", StringComparison.Ordinal) < body.IndexOf("command -v yum", StringComparison.Ordinal),
			"dnf is tried before yum");
	}

	[Fact]
	public async Task TheScript_AndEveryCachedUpdateCheck_ParseInAPosixShell()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		List<AdminScript> scripts = [SurveyCommands.Script];
		scripts.AddRange(SystemProfile.PackageManagerSearchOrder.Select(kind => new PackageCommands(kind).ListCachedUpdates()));
		foreach (AdminScript script in scripts)
		{
			string text = AdminScriptText.Compose(script);
			(int exitCode, _, string error) = await PosixShellLocator
				.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "sh -n <<'MOKATERM_SCRIPT'\n" + text + "\nMOKATERM_SCRIPT\n", [])
				.WaitAsync(ShellTimeout, TestContext.Current.CancellationToken);

			Assert.True(exitCode == 0, $"{script.Name}: {error}");
		}
	}

	[Fact]
	public async Task TheScript_RunsInALocalShell_AndEveryPartReports()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		// A docker on the test machine (the CI runner has one) answers as slowly as it likes, and a part running out of time
		// is noted in the summary. The time limits are the script's business, not this test's, so the survey meets a docker
		// that refuses at once.
		const string RefusingDocker =
			"mokaterm_fake=$(mktemp -d) && printf '#!/bin/sh\\nexit 1\\n' > \"$mokaterm_fake/docker\" && chmod +x \"$mokaterm_fake/docker\" && PATH=\"$mokaterm_fake:$PATH\" && export PATH\n";
		(int exitCode, byte[] output, string error) = await PosixShellLocator
			.RunAsync(shell, PosixShellLocator.UnixPathPrefix + RefusingDocker + AdminScriptText.Compose(SurveyCommands.Script), [])
			.WaitAsync(ShellTimeout, TestContext.Current.CancellationToken);

		Assert.True(exitCode == 0, error);
		Assert.True(RemoteOutput.TryGetPayload(output, out ReadOnlyMemory<byte> payload));
		string text = Encoding.UTF8.GetString(payload.Span);
		foreach (string part in (string[])["probe", "overview", "updates", "services", "reboot", "docker"])
		{
			Assert.Contains($"{SurveyCommands.PartPrefix}exit {part} ", text, StringComparison.Ordinal);
		}

		HostSummary summary = SurveyCommands.Parse(text);
		Assert.NotNull(summary.OperatingSystem);
		Assert.NotNull(summary.Kernel);
		Assert.Empty(summary.Notes);
	}

	[Fact]
	public async Task TheFeature_ReadsThroughTheCommandFeature_AsTheLogin()
	{
		ScriptedCommands commands = new("Last login: today\nMOKATERM-OUT\0" + SurveyFixtures.Ubuntu());
		SshHostSummaryFeature feature = new(commands);

		HostSummary summary = await feature.ReadSummaryAsync(TestContext.Current.CancellationToken);

		Assert.Equal("Ubuntu 24.04.1 LTS", summary.OperatingSystem);
		RemoteCommandRequest request = Assert.Single(commands.Requests);
		Assert.Contains(AdminScriptText.TagPrefix + "survey", request.Command, StringComparison.Ordinal);
		Assert.StartsWith("sh -c ", request.Command, StringComparison.Ordinal);
		Assert.Equal(SurveyCommands.Script.OutputLimitBytes, request.OutputLimitBytes);
	}

	[Fact]
	public async Task TheFeature_SaysWhenTheScriptNeverRan()
	{
		SshHostSummaryFeature feature = new(new ScriptedCommands("This account is currently not available.\n"));

		RemoteCommandException refused = await Assert.ThrowsAsync<RemoteCommandException>(
			() => feature.ReadSummaryAsync(TestContext.Current.CancellationToken));
		Assert.StartsWith("The server did not run the script", refused.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheFeature_NotesOutputThatWasCut()
	{
		SshHostSummaryFeature feature = new(new ScriptedCommands("MOKATERM-OUT\0" + SurveyFixtures.Ubuntu(), extraBytes: 5000));

		HostSummary summary = await feature.ReadSummaryAsync(TestContext.Current.CancellationToken);

		Assert.Contains(summary.Notes, note => note.Contains("more than the read keeps", StringComparison.Ordinal));
	}

	/// <summary>Answers every command with the same stdout, counting extra bytes that were not kept when asked to.</summary>
	private sealed class ScriptedCommands(string output, int extraBytes = 0) : IRemoteCommandFeature
	{
		private readonly List<RemoteCommandRequest> _requests = [];

		public IReadOnlyList<RemoteCommandRequest> Requests => _requests;

		public Task<RemoteCommandOutcome> RunAsync(RemoteCommandRequest request, CancellationToken cancellationToken)
		{
			_requests.Add(request);
			byte[] bytes = Encoding.UTF8.GetBytes(output);
			return Task.FromResult(new RemoteCommandOutcome
			{
				ExitCode = 0,
				Output = new CapturedOutput(bytes, bytes.Length + extraBytes),
				Error = CapturedOutput.Empty,
			});
		}
	}
}
