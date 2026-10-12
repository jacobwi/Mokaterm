using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

public sealed class SystemAdminFeatureTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

	[Fact]
	public async Task Detection_RunsOnce_UntilAskedAgain()
	{
		FakeAdminShell shell = new();
		await using SystemAdminFeature feature = new(shell, TimeProvider.System, NullLogger.Instance);

		SystemProfile first = await feature.DetectAsync(refresh: false, TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);
		SystemProfile second = await feature.DetectAsync(refresh: false, TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);
		SystemProfile third = await feature.DetectAsync(refresh: true, TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);

		Assert.Same(first, second);
		Assert.NotSame(first, third);
		Assert.Equal(2, shell.Runs.Count(run => run.Script.Name == "detect"));
		Assert.Same(third, feature.Profile);
		Assert.Equal(DistroFamily.Debian, first.Family);
	}

	[Fact]
	public async Task Detection_ThatFailed_IsTriedAgain()
	{
		FakeAdminShell shell = new() { FailNext = new AdminException("The connection to the server was lost.") };
		await using SystemAdminFeature feature = new(shell, TimeProvider.System, NullLogger.Instance);

		await Assert.ThrowsAsync<AdminException>(() => feature.DetectAsync(false, TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken));
		SystemProfile profile = await feature.DetectAsync(false, TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);

		Assert.Equal(PackageManagerKind.Apt, profile.PackageManager);
	}

	[Fact]
	public async Task Reads_AsRoot_NeedRootMode_AndNeverPromptOnTheirOwn()
	{
		FakeAdminShell shell = new();
		await using SystemAdminFeature feature = new(shell, TimeProvider.System, NullLogger.Instance);
		await feature.DetectAsync(false, TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);
		AdminScript script = new() { Name = "firewall.status", Body = "ufw status", AsRoot = true };

		AdminException refused = await Assert.ThrowsAsync<AdminException>(() => feature.ReadAsync(script, TestContext.Current.CancellationToken));
		Assert.Equal(AdminTexts.RootModeNeeded, refused.Message);
		Assert.Equal(0, shell.RootPrompts);

		await feature.EnableRootAsync(TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);
		await feature.ReadAsync(script, TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);
		Assert.True(shell.Runs[^1].Elevated);

		feature.DisableRoot();
		Assert.False(feature.RootModeOn);
	}

	[Fact]
	public async Task RootLogin_RunsRootScriptsWithoutSudo()
	{
		FakeAdminShell shell = new() { DetectOutput = DetectionFixtures.Ubuntu2404 };
		await using SystemAdminFeature feature = new(shell, TimeProvider.System, NullLogger.Instance);
		await feature.DetectAsync(false, TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);

		await feature.ReadAsync(new AdminScript { Name = "firewall.status", Body = "ufw status", AsRoot = true }, TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);

		Assert.True(feature.LoginIsRoot);
		Assert.True(feature.CanActAsRoot);
		Assert.False(shell.Runs[^1].Elevated);
		Assert.Equal(0, shell.RootPrompts);
	}

	[Fact]
	public async Task Actions_TurnRootOnFirst_AndKeepTheirResult()
	{
		FakeAdminShell shell = new();
		await using SystemAdminFeature feature = new(shell, TimeProvider.System, NullLogger.Instance);
		await feature.DetectAsync(false, TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);
		int changes = 0;
		feature.Changed += () => Interlocked.Increment(ref changes);

		AdminActionResult result = await feature.RunAsync(new AdminAction
		{
			Title = "Restart cron",
			Script = new AdminScript { Name = "services.restart", Body = "systemctl restart -- 'cron.service'", AsRoot = true },
		}, input: null).WaitAsync(Wait, TestContext.Current.CancellationToken);

		Assert.Equal(1, shell.RootPrompts);
		Assert.True(result.Succeeded);
		Assert.Equal("systemctl restart -- 'cron.service'", result.Commands);
		Assert.Same(result, feature.Last);
		Assert.Null(feature.Running);
		Assert.True(changes >= 2);
	}

	[Fact]
	public async Task Actions_ThatCouldNotRun_RecordWhy()
	{
		FakeAdminShell shell = new();
		await using SystemAdminFeature feature = new(shell, TimeProvider.System, NullLogger.Instance);
		await feature.DetectAsync(false, TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);
		shell.FailNext = new AdminException("sudo did not accept the password for alice@web1.") { RootRefused = true };

		AdminActionResult result = await feature.RunAsync(new AdminAction
		{
			Title = "Stop nginx",
			Script = new AdminScript { Name = "services.stop", Body = "systemctl stop -- 'nginx.service'", AsRoot = true },
		}, input: null).WaitAsync(Wait, TestContext.Current.CancellationToken);

		Assert.False(result.Succeeded);
		Assert.False(result.IsRunning);
		Assert.Equal("sudo did not accept the password for alice@web1.", result.Failure);
		Assert.Null(feature.Running);
	}

	[Fact]
	public async Task Actions_RunOneAtATime()
	{
		FakeAdminShell shell = new() { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
		await using SystemAdminFeature feature = new(shell, TimeProvider.System, NullLogger.Instance);
		shell.Gate.SetResult();
		await feature.DetectAsync(false, TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);
		shell.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		AdminAction action = new() { Title = "Long", Script = new AdminScript { Name = "packages.upgrade", Body = "apt-get upgrade" } };

		Task<AdminActionResult> first = feature.RunAsync(action, input: null);
		Assert.NotNull(feature.Running);
		await Assert.ThrowsAsync<AdminException>(() => feature.RunAsync(action, input: null));

		shell.Gate.SetResult();
		Assert.True((await first.WaitAsync(Wait, TestContext.Current.CancellationToken)).Succeeded);
	}

	[Fact]
	public async Task ClosedFeature_RefusesWork()
	{
		SystemAdminFeature feature = new(new FakeAdminShell(), TimeProvider.System, NullLogger.Instance);
		await feature.DisposeAsync();

		Assert.Throws<AdminException>(() => { _ = feature.DetectAsync(false, TestContext.Current.CancellationToken); });
	}
}
