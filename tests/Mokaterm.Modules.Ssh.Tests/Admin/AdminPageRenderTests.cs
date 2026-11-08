using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Admin.Monitoring;
using Mokaterm.Modules.Ssh.Components.Admin;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Mokaterm.UI.Common.Editing;
using Mokaterm.UI.Common.Extensions;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

// The System view's lists, rendered for real over a server answered from fixtures: each one is a table that sorts, keys
// and empties itself, and nothing in them is cut to a row count any more.
public sealed partial class AdminPageRenderTests
{
	private const string Accounts = """
		@@mt:passwd
		root:x:0:0:root:/root:/bin/bash
		daemon:x:1:1:daemon:/usr/sbin:/usr/sbin/nologin
		bob:x:1001:1001::/home/bob:/bin/sh
		alice:x:1000:1000:Alice Liddell,,,:/home/alice:/bin/bash
		@@mt:group
		root:x:0:
		sudo:x:27:alice
		alice:x:1000:
		bob:x:1001:
		@@mt:shells
		/bin/sh
		/bin/bash
		@@mt:logindefs
		UID_MIN			 1000
		@@mt:shadow
		root:$
		daemon:*
		alice:$
		bob:!
		""";

	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task TheUsersPage_ListsPeopleByUid_WithTheirPasswordStates()
	{
		string html = await RenderAsync<AdminUsersPage>(script => script.Name == "accounts.read" ? Ok(Accounts) : null);

		Assert.Contains("aria-label=\"People\"", html, StringComparison.Ordinal);
		Assert.Equal(["root", "alice", "bob"], MonoNames(html, ["root", "alice", "bob", "daemon"]));
		Assert.Matches(UidSorted(), html);
		Assert.Contains(">Password<", html, StringComparison.Ordinal);
		Assert.Contains(">locked<", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">LOCKED<", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mta-row", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheProcessesPage_ListsEveryProcess_BusiestFirst()
	{
		string html = await RenderAsync<AdminProcessesPage>(script => script.Name == "processes.list" ? Ok(Processes(40)) : null);

		Assert.Contains("aria-label=\"Processes\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-sort=\"descending\"", html, StringComparison.Ordinal);
		int nginx = html.IndexOf("nginx: worker process", StringComparison.Ordinal);
		Assert.True(nginx > 0, "the busiest process is drawn");
		Assert.True(nginx < html.IndexOf("idle-100", StringComparison.Ordinal), "and drawn first");
		Assert.Contains("idle-139", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Filter to find others", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ALongProcessList_DrawsOnlyTheRowsInView()
	{
		string html = await RenderAsync<AdminProcessesPage>(script => script.Name == "processes.list" ? Ok(Processes(2000)) : null);

		Assert.Contains("aria-label=\"Processes\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("idle-2099", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Filter to find others", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheCronPage_PutsLinesCronCannotRead_AboveItsJobs()
	{
		const string Crontab = "0 3 * * * /usr/local/bin/backup.sh\n61 * * * * /bin/true\n";

		string html = await RenderAsync<AdminCronPage>(script => script.Name == "cron.read" ? Ok(Crontab) : null);

		int warning = html.IndexOf("Lines cron cannot read as a job", StringComparison.Ordinal);
		int table = html.IndexOf("<table", StringComparison.Ordinal);
		Assert.True(warning > 0 && table > warning, "the warning sits above the table");
		Assert.Contains("line 2: 61 * * * * /bin/true", html, StringComparison.Ordinal);
		Assert.Contains("/usr/local/bin/backup.sh", html, StringComparison.Ordinal);
		Assert.Contains("s crontab\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AnEmptyCrontab_SaysSoInsideItsTable()
	{
		string html = await RenderAsync<AdminCronPage>(script => script.Name == "cron.read" ? new AdminOutcome { ExitCode = 1, Error = "no crontab for alice\n" } : null);

		Assert.Contains("Nothing scheduled", html, StringComparison.Ordinal);
		Assert.Contains("<table", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheFirewallPage_ListsUfwRules_InTheirOrder_UnderItsPolicies()
	{
		const string Numbered = """
			Status: active

			     To                         Action      From
			     --                         ------      ----
			[ 1] 22/tcp                     ALLOW IN    Anywhere
			[ 2] 80,443/tcp                 ALLOW IN    Anywhere
			[ 3] 3306                       DENY IN     10.0.0.0/8
			[ 4] 22/tcp (v6)                ALLOW IN    Anywhere (v6)

			""";
		string status = "@@mt:verbose\n" + FirewallFixtures.UfwActive + "@@mt:numbered\n" + Numbered;

		string html = await RenderAsync<AdminFirewallPage>(
			script => script.Name == "firewall.status" ? Ok(status) : null,
			asRoot: true);

		Assert.Contains("aria-label=\"Firewall rules\"", html, StringComparison.Ordinal);
		Assert.True(
			html.IndexOf("Default", StringComparison.Ordinal) < html.IndexOf("<table", StringComparison.Ordinal),
			"the policies sit above the rules");
		int ssh = html.IndexOf(">22/tcp<", StringComparison.Ordinal);
		int web = html.IndexOf(">80,443/tcp<", StringComparison.Ordinal);
		int mysql = html.IndexOf(">3306<", StringComparison.Ordinal);
		Assert.True(ssh > 0 && ssh < web && web < mysql, "the rules keep the order ufw applies them in");
		Assert.Contains(">#<", html, StringComparison.Ordinal);
		Assert.Contains("moka-tag--error", html, StringComparison.Ordinal);
		Assert.Contains(">Default<", html, StringComparison.Ordinal);
	}

	private static AdminOutcome Ok(string output) => new() { ExitCode = 0, Output = output.ReplaceLineEndings("\n") };

	private static string Processes(int idle)
	{
		StringBuilder output = new("@@mt:memtotal\nMemTotal:        8037888 kB\n@@mt:procps\n");
		for (int pid = 100; pid < 100 + idle; pid++)
		{
			output.Append(CultureInfo.InvariantCulture, $"  {pid} root  0.0  0.1 1024 S  idle-{pid}\n");
		}

		return output.Append("  2211 www-data  12.5  3.2 262144 Sl  nginx: worker process\n").ToString();
	}

	private static List<string> MonoNames(string html, IEnumerable<string> candidates) =>
	[
		.. candidates
			.Select(name => (Name: name, At: html.IndexOf($">{name}</span>", StringComparison.Ordinal)))
			.Where(found => found.At >= 0)
			.OrderBy(found => found.At)
			.Select(found => found.Name),
	];

	[GeneratedRegex("""aria-sort="ascending"[^>]*>(?:(?!</th>).)*UID""", RegexOptions.Singleline)]
	private static partial Regex UidSorted();

	private static async Task<string> RenderAsync<TPage>(Func<AdminScript, AdminOutcome?> answer, bool asRoot = false)
		where TPage : IComponent
	{
		FakeAdminShell shell = new() { Answer = script => answer(script) ?? new AdminOutcome { ExitCode = 0 } };
		await using SystemAdminFeature feature = new(shell, TimeProvider.System, NullLogger.Instance);
		SystemProfile profile = await feature.DetectAsync(false, Ct).WaitAsync(Wait, Ct);
		if (asRoot)
		{
			await feature.EnableRootAsync(Ct).WaitAsync(Wait, Ct);
		}
		AdminPageContext context = new()
		{
			Feature = feature,
			Profile = profile,
			Monitor = new HostMonitor(feature.ReadAsync, TimeProvider.System, NullLogger.Instance, toolKey: null),
			Run = async (action, input) => await feature.RunAsync(action, input),
			EnableRoot = () => feature.EnableRootAsync(CancellationToken.None),
			Show = _ => { },
			SessionAddress = "web1",
			IsBusy = () => false,
			IsVisible = () => true,
			Reboot = () => Task.CompletedTask,
			Login = () => Task.FromResult(new SessionLogin("alice", null, null)),
			Editor = new NoEditor(),
		};

		RenderFragment page = builder =>
		{
			builder.OpenComponent<TPage>(0);
			builder.CloseComponent();
		};

		return await StaticRender.RenderAsync<CascadingValue<AdminPageContext>>(
			services =>
			{
				services.AddSingleton(TimeProvider.System);
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<ISettingsService>(new FakeSettingsService());
				services.AddSingleton<IRemoteTextEditor>(new NoEditor());
			},
			new Dictionary<string, object?>
			{
				[nameof(CascadingValue<AdminPageContext>.Value)] = context,
				[nameof(CascadingValue<AdminPageContext>.ChildContent)] = page,
			})
			.WaitAsync(Wait, Ct);
	}

	private sealed class NoEditor : IRemoteTextEditor
	{
		public Task<RemoteTextEditResult> EditAsync(RemoteTextEditRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult(RemoteTextEditResult.Closed);
	}
}
