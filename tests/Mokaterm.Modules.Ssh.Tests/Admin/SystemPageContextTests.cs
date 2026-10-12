using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Components.Admin;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Editing;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>
/// What a System view page from another module gets: the server's facts, reads and reviewed changes through the same
/// feature, limits and review as the view's own pages, navigation by id, and the built-in pages as descriptors whose
/// rules grey what the server lacks.
/// </summary>
public sealed class SystemPageContextTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

	[Fact]
	public async Task Server_IsWhatDetectionFound()
	{
		(AdminPageContext context, _, SystemAdminFeature feature) = await ContextAsync();
		await using (feature)
		{
			ServerFacts server = ((ISystemPageContext)context).Server;

			Assert.Equal("debian", server.DistributionId);
			Assert.Equal("12", server.DistributionVersion);
			Assert.Equal("systemd", server.InitSystem);
			Assert.Equal("apt", server.PackageManager);
			Assert.Equal("alice", server.UserName);
			Assert.False(server.IsRootLogin);
			Assert.True(server.HasCommand("systemctl"));
			Assert.False(server.HasCommand("fail2ban-client"));
			Assert.DoesNotContain("web1", server.ToString(), StringComparison.Ordinal);
		}
	}

	[Fact]
	public async Task ARead_RunsThroughTheFeature_WithTheViewsReadLimits()
	{
		FakeAdminShell shell = new() { Answer = _ => new AdminOutcome { ExitCode = 3, Output = "listed\n", Error = "jail sshd is gone\n" } };
		(AdminPageContext context, _, SystemAdminFeature feature) = await ContextAsync(shell);
		await using (feature)
		{
			SystemScriptResult result = await ((ISystemPageContext)context)
				.ReadAsync(new SystemScript { Name = "fail2ban.status", Body = "fail2ban-client status" }, TestContext.Current.CancellationToken)
				.WaitAsync(Wait, TestContext.Current.CancellationToken);

			(AdminScript script, bool elevated) = shell.Runs[^1];
			Assert.Equal("fail2ban.status", script.Name);
			Assert.Equal("fail2ban-client status", script.Body);
			Assert.Equal(AdminScript.ReadTimeout, script.Timeout);
			Assert.Equal(AdminScript.ReadOutputBytes, script.OutputLimitBytes);
			Assert.False(script.ReadsInput);
			Assert.False(elevated);
			Assert.False(result.Succeeded);
			Assert.Equal(3, result.ExitCode);
			Assert.Equal("listed\n", result.Output);
			Assert.Equal("jail sshd is gone", result.Problem);
		}
	}

	[Fact]
	public async Task AReadAsRoot_NeedsRootMode_AndNeverAsksOnItsOwn()
	{
		FakeAdminShell shell = new();
		(AdminPageContext context, _, SystemAdminFeature feature) = await ContextAsync(shell);
		await using (feature)
		{
			ISystemPageContext page = context;
			SystemScript script = new() { Name = "fail2ban.status", Body = "fail2ban-client status", AsRoot = true };

			Assert.False(page.CanReadAsRoot);
			await Assert.ThrowsAnyAsync<SystemScriptException>(() => page.ReadAsync(script, TestContext.Current.CancellationToken));
			Assert.Equal(0, shell.RootPrompts);

			await page.EnableRootAsync().WaitAsync(Wait, TestContext.Current.CancellationToken);
			Assert.True(page.RootModeOn);
			Assert.True(page.CanReadAsRoot);
			await page.ReadAsync(script, TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);
			Assert.True(shell.Runs[^1].Elevated);
		}
	}

	[Theory]
	[InlineData("")]
	[InlineData("two words")]
	[InlineData("fail2ban.status\nrm -rf /")]
	[InlineData(".hidden")]
	public void AScriptName_ThatIsNoName_IsRefused(string name) =>
		Assert.Throws<ArgumentException>(() => SystemPageScripts.ToAdminScript(new SystemScript { Name = name, Body = "true" }, change: false, readsInput: false));

	[Fact]
	public void AnEmptyBodyOrANul_IsRefused()
	{
		Assert.Throws<ArgumentException>(() => SystemPageScripts.ToAdminScript(new SystemScript { Name = "x", Body = " " }, change: false, readsInput: false));
		Assert.Throws<ArgumentException>(() => SystemPageScripts.ToAdminScript(new SystemScript { Name = "x", Body = "echo \0" }, change: false, readsInput: false));
	}

	[Fact]
	public void Limits_AreTheViewsOwn_AndCappedThere()
	{
		AdminScript change = SystemPageScripts.ToAdminScript(new SystemScript { Name = "x", Body = "true" }, change: true, readsInput: false);
		AdminScript greedy = SystemPageScripts.ToAdminScript(
			new SystemScript { Name = "x", Body = "true", Timeout = TimeSpan.FromHours(5), OutputLimitBytes = int.MaxValue },
			change: false,
			readsInput: false);
		AdminScript hasty = SystemPageScripts.ToAdminScript(new SystemScript { Name = "x", Body = "true", Timeout = TimeSpan.Zero, OutputLimitBytes = -5 }, change: false, readsInput: false);

		Assert.Equal(AdminScript.ActionTimeout, change.Timeout);
		Assert.Equal(AdminScript.ActionOutputBytes, change.OutputLimitBytes);
		Assert.Equal(AdminScript.PackageTimeout, greedy.Timeout);
		Assert.Equal(AdminScript.ReadOutputBytes, greedy.OutputLimitBytes);
		Assert.Equal(TimeSpan.FromSeconds(1), hasty.Timeout);
		Assert.Equal(1, hasty.OutputLimitBytes);
	}

	[Fact]
	public async Task AChange_GoesThroughTheViewsReview_WithASecretInputMasked()
	{
		FakeAdminShell shell = new();
		List<(AdminAction Action, string? Preview)> reviewed = [];
		(AdminPageContext context, _, SystemAdminFeature feature) = await ContextAsync(shell, run: async (action, input, feature) =>
		{
			reviewed.Add((action, input?.Preview));
			return await feature.RunAsync(action, input);
		});
		await using (feature)
		{
			SystemChangeResult? result = await ((ISystemPageContext)context).RunChangeAsync(new SystemChange
			{
				Title = "Set the password",
				Script = new SystemScript { Name = "users.password", Body = "chpasswd", AsRoot = true },
				Destructive = true,
				ConfirmText = "Set",
				Input = "alice:hunter2",
				InputPreview = "alice:********",
			}).WaitAsync(Wait, TestContext.Current.CancellationToken);

			(AdminAction action, string? preview) = Assert.Single(reviewed);
			Assert.Equal("Set the password", action.Title);
			Assert.Equal("Set", action.ConfirmText);
			Assert.True(action.Destructive);
			Assert.True(action.Script.AsRoot);
			Assert.True(action.Script.ReadsInput);
			Assert.Equal(AdminScript.ActionTimeout, action.Script.Timeout);
			Assert.Equal("alice:********", preview);
			Assert.Equal("alice:********", shell.Inputs[^1]);
			Assert.NotNull(result);
			Assert.True(result.Succeeded);
			Assert.Equal("Set the password", result.Title);
		}
	}

	[Fact]
	public async Task AChangeTheUserCancelled_IsNull()
	{
		(AdminPageContext context, _, SystemAdminFeature feature) = await ContextAsync(run: (_, _, _) => Task.FromResult<AdminActionResult?>(null));
		await using (feature)
		{
			SystemChangeResult? result = await ((ISystemPageContext)context).RunChangeAsync(new SystemChange
			{
				Title = "Restart cron",
				Script = new SystemScript { Name = "services.restart", Body = "systemctl restart cron", AsRoot = true },
			});

			Assert.Null(result);
		}
	}

	[Fact]
	public async Task Navigation_IsByPageId_WithAHintTheNextPageTakesOnce()
	{
		List<string> shown = [];
		(AdminPageContext context, _, SystemAdminFeature feature) = await ContextAsync(show: shown.Add);
		await using (feature)
		{
			context.Navigate(AdminPageIds.Services, "failed");
			Assert.Equal(["services"], shown);
			Assert.Equal("failed", context.TakeHint());
			Assert.Null(context.TakeHint());

			context.Navigate("fail2ban");
			Assert.Equal(["services", "fail2ban"], shown);
			Assert.Null(context.TakeHint());
		}
	}

	[Fact]
	public void TheBuiltInPages_AreDescriptors_InTheirOrder()
	{
		using ServiceProvider services = ModuleServices();
		IUiContributions contributions = services.GetRequiredService<IUiContributions>();

		Assert.Equal(
			["overview", "services", "packages", "cron", "time", "hostname", "network", "users", "disks", "processes", "logs", "firewall", "security"],
			contributions.SystemPages.Select(page => page.Id));
		Assert.All(contributions.SystemPages, page => Assert.True(typeof(AdminPageBase).IsAssignableFrom(page.Component)));
		Assert.Equal(typeof(AdminOverviewPage), contributions.SystemPages[0].Component);
	}

	[Fact]
	public async Task TheBuiltInRules_GreyWhatTheServerLacks()
	{
		using ServiceProvider services = ModuleServices();
		IReadOnlyList<SystemPageDescriptor> pages = services.GetRequiredService<IUiContributions>().SystemPages;

		(AdminPageContext debian, _, SystemAdminFeature debianFeature) = await ContextAsync();
		(AdminPageContext container, _, SystemAdminFeature containerFeature) = await ContextAsync(new FakeAdminShell { DetectOutput = DetectionFixtures.AlpineContainer });
		await using (debianFeature)
		await using (containerFeature)
		{
			Assert.All(pages, page => Assert.Null(page.Unavailable?.Invoke(debian)));

			SystemPageDescriptor servicesPage = Assert.Single(pages, page => page.Id == AdminPageIds.Services);
			Assert.Equal(container.Profile.Capabilities.ServicesMissing, servicesPage.Unavailable?.Invoke(container));
			Assert.Null(Assert.Single(pages, page => page.Id == AdminPageIds.Overview).Unavailable?.Invoke(container));

			// The rules read the view's own context; anything else has nothing they could judge.
			Assert.Null(servicesPage.Unavailable?.Invoke(new ForeignContext()));
		}
	}

	[Fact]
	public void TheProbe_LooksForTheCommandsPagesName_AfterItsOwn()
	{
		AdminScript probe = SystemProbe.ScriptFor(["fail2ban-client", "systemctl", "fail2ban-client", "nginx", "bad name", "$(id)"]);

		Assert.Contains("for c in " + string.Join(' ', SystemProbe.ProbedCommands) + " fail2ban-client nginx; do", probe.Body, StringComparison.Ordinal);
		Assert.DoesNotContain("bad name", probe.Body, StringComparison.Ordinal);
		Assert.DoesNotContain("$(id)", probe.Body, StringComparison.Ordinal);
		Assert.Equal(SystemProbe.Script.Body, SystemProbe.ScriptFor([]).Body);

		const string Output = "@@mt:commands\nsystemctl\nfail2ban-client\nnginx\n";
		Assert.True(SystemProbe.Parse(Output, ["fail2ban-client"]).Has("fail2ban-client"));
		Assert.False(SystemProbe.Parse(Output, ["fail2ban-client"]).Has("nginx"));
		Assert.False(SystemProbe.Parse(Output).Has("fail2ban-client"));
	}

	[Fact]
	public async Task Detection_TakesTheExtraCommands_IntoTheFacts()
	{
		FakeAdminShell shell = new() { DetectOutput = DetectionFixtures.Debian12.ReplaceLineEndings("\n").Replace("\nufw\n", "\nufw\nfail2ban-client\n", StringComparison.Ordinal) };
		await using SystemAdminFeature feature = new(shell, TimeProvider.System, NullLogger.Instance);

		SystemProfile profile = await feature.DetectAsync(false, ["fail2ban-client"], TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);

		Assert.True(profile.ServerFacts.HasCommand("fail2ban-client"));
		Assert.Contains("fail2ban-client; do", shell.Runs[0].Script.Body, StringComparison.Ordinal);
	}

	private static ServiceProvider ModuleServices()
	{
		ServiceCollection collection = new();
		collection.AddSingleton<ISettingsService>(new FakeSettingsService());
		new SshModule().ConfigureServices(collection);
		return collection.BuildServiceProvider();
	}

	private static async Task<(AdminPageContext Context, FakeAdminShell Shell, SystemAdminFeature Feature)> ContextAsync(
		FakeAdminShell? shell = null,
		Func<AdminAction, AdminInput?, SystemAdminFeature, Task<AdminActionResult?>>? run = null,
		Action<string>? show = null)
	{
		shell ??= new FakeAdminShell();
		SystemAdminFeature feature = new(shell, TimeProvider.System, NullLogger.Instance);
		SystemProfile profile = await feature.DetectAsync(false, TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken);
		Func<AdminAction, AdminInput?, Task<AdminActionResult?>> review = run is null
			? async (action, input) => await feature.RunAsync(action, input)
			: (action, input) => run(action, input, feature);
		AdminPageContext context = new()
		{
			Feature = feature,
			Profile = profile,
			Run = review,
			EnableRoot = () => feature.EnableRootAsync(CancellationToken.None),
			Show = show ?? (_ => { }),
			SessionAddress = "web1",
			IsBusy = () => false,
			IsVisible = () => true,
			Reboot = () => Task.CompletedTask,
			Login = () => Task.FromResult(new SessionLogin("alice", null, null)),
			Editor = new NoEditor(),
		};

		return (context, shell, feature);
	}

	private sealed class NoEditor : IRemoteTextEditor
	{
		public Task<RemoteTextEditResult> EditAsync(RemoteTextEditRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult(RemoteTextEditResult.Closed);
	}

	/// <summary>A context from some other view, which the built-in rules know nothing about.</summary>
	private sealed class ForeignContext : ISystemPageContext
	{
		public ServerFacts Server { get; } = new() { DisplayName = "Elsewhere" };

		public bool RootModeOn => false;

		public bool CanReadAsRoot => false;

		public string? RootChangeBlocked => null;

		public bool IsBusy => false;

		public bool IsVisible => true;

		public Task EnableRootAsync() => Task.CompletedTask;

		public void Navigate(string pageId, string? hint = null)
		{
		}

		public string? TakeHint() => null;

		public Task<SystemScriptResult> ReadAsync(SystemScript script, CancellationToken cancellationToken) => Task.FromResult(new SystemScriptResult());

		public Task<SystemChangeResult?> RunChangeAsync(SystemChange change) => Task.FromResult<SystemChangeResult?>(null);

		public Task<RemoteTextEditResult> EditFileAsync(string path, bool asRoot, bool createNew, CancellationToken cancellationToken) =>
			Task.FromResult(RemoteTextEditResult.Closed);
	}
}
