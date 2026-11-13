using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Moka.Red.Primitives.DataList;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Ssh.Components.Admin;
using Mokaterm.UI.Common.Extensions;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

// The pages drawn with Moka.Red's own figures, facts and outputs rather than the hand-made cards and key-value lists
// they replaced.
public sealed partial class AdminPageRenderTests
{
	private const string OverviewOutput = """
		@@mt:uptime
		3456000.00 12000000.00
		@@mt:stat
		cpu  4000 100 2000 90000 500 0 300 0 0 0
		@@mt:loadavg
		0.90 0.60 0.45 2/318 48211
		@@mt:meminfo
		MemTotal:        8037888 kB
		MemFree:          912000 kB
		MemAvailable:    5122000 kB
		Buffers:          182044 kB
		Cached:          3402112 kB
		SwapTotal:       2097148 kB
		SwapFree:        1897148 kB
		@@mt:rootfs
		Filesystem     1024-blocks     Used Available Capacity Mounted on
		/dev/vda1         81971444 38213332  43741728      47% /
		@@mt:now
		1794000000
		@@mt:cpu
		model name	: AMD EPYC 7763 64-Core Processor
		@@mt:cores
		4
		@@mt:df
		Filesystem     1024-blocks     Used Available Capacity Mounted on
		/dev/vda1         81971444 38213332  43741728      47% /
		/dev/vdb         209612800 191232000  18380800      92% /srv/data
		@@mt:mounts
		/dev/vda1 / ext4 rw,relatime 0 0
		/dev/vdb /srv/data xfs rw,relatime 0 0
		""";

	[Fact]
	public async Task TheOverview_DrawsGaugesChartsTilesAndFacts_InsteadOfCards()
	{
		string html = await RenderAsync<AdminOverviewPage>(script => script.Name == "overview.read" ? Ok(OverviewOutput) : null);

		Assert.Contains("aria-label=\"Memory used\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Root file system used\"", html, StringComparison.Ordinal);
		Assert.Contains("moka-gauge--ring", html, StringComparison.Ordinal);
		Assert.True(Count(html, "class=\"moka-linechart ") >= 2, "the CPU and memory charts are drawn");
		Assert.Contains("moka-stat--tile", html, StringComparison.Ordinal);
		Assert.Contains(">Uptime<", html, StringComparison.Ordinal);
		Assert.Contains("AMD EPYC 7763 64-Core Processor", html, StringComparison.Ordinal);
		Assert.Contains("moka-data-list", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"/srv/data\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mta-card", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mt-sparkline", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheOverview_ColoursAFullDisk_TheWayTheUsageBarWould()
	{
		string html = await RenderAsync<AdminOverviewPage>(script => script.Name == "overview.read" ? Ok(OverviewOutput) : null);

		Match meter = SrvDataMeter().Match(html);
		Assert.True(meter.Success, "the 92% file system has a meter");
		Assert.Contains("moka-meter--error", meter.Value, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheTimePage_ShowsTilesAndFacts()
	{
		long epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		string clock = string.Create(CultureInfo.InvariantCulture, $"""
			@@mt:date
			2026-11-08 06:28:29 UTC (+0000)
			@@mt:epoch
			{epoch}
			@@mt:show
			Timezone=Europe/Berlin
			LocalRTC=no
			CanNTP=yes
			NTP=yes
			NTPSynchronized=yes
			""");

		string html = await RenderAsync<AdminTimePage>(script => script.Name == "time.read" ? Ok(clock) : null);

		Assert.Contains(">06:28:29<", html, StringComparison.Ordinal);
		Assert.Contains(">In step<", html, StringComparison.Ordinal);
		Assert.Contains(">Synchronized<", html, StringComparison.Ordinal);
		Assert.Contains("moka-stat--tile", html, StringComparison.Ordinal);
		Assert.Contains("moka-data-list", html, StringComparison.Ordinal);
		Assert.Contains("Europe/Berlin", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mta-kv", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheHostnamePage_MarksANameThatChangesAtTheNextBoot()
	{
		const string Names = """
			@@mt:current
			web1
			@@mt:file
			web2
			@@mt:ctl
			 Static hostname: web2
			         Chassis: vm
			""";

		string html = await RenderAsync<AdminHostNamePage>(script => script.Name == "hostname.read" ? Ok(Names) : null);

		Assert.Contains(">web1<", html, StringComparison.Ordinal);
		Assert.Contains("changes at the next boot", html, StringComparison.Ordinal);
		Assert.Contains(">Differs<", html, StringComparison.Ordinal);
		Assert.Contains(">Chassis<", html, StringComparison.Ordinal);
		Assert.Contains("Rename the machine", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mta-kv", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AnOutput_DrawsMokasCodeBlock_WithOurCopyButtonAndNotItsOwn()
	{
		string html = await RenderComponentAsync<AdminOutput>(new()
		{
			[nameof(AdminOutput.Label)] = "Status",
			[nameof(AdminOutput.Text)] = "* nginx.service - A high performance web server\n   Active: active (running)",
			[nameof(AdminOutput.CopyLabel)] = "Copy the status",
			[nameof(AdminOutput.CodeId)] = "status-out",
		});

		Assert.Contains("moka-code-block", html, StringComparison.Ordinal);
		Assert.Contains("id=\"status-out\"", html, StringComparison.Ordinal);
		Assert.Contains("Active: active (running)", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-code-block__copy", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy the status\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AnOutputBeingRead_ShowsWhatItWasHandedInstead_AndNothingToCopy()
	{
		RenderFragment pending = builder => builder.AddContent(0, "Reading the status");

		string html = await RenderComponentAsync<AdminOutput>(new()
		{
			[nameof(AdminOutput.Label)] = "Status",
			[nameof(AdminOutput.Text)] = null,
			[nameof(AdminOutput.ChildContent)] = pending,
		});

		Assert.Contains("Reading the status", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-code-block", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Copy the output", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Facts_AreMokasDataList_LabelBesideValue()
	{
		string html = await RenderComponentAsync<AdminFacts>(new()
		{
			[nameof(AdminFacts.Title)] = "Policies",
			[nameof(AdminFacts.Items)] = new MokaDataListItem[] { new("Default", "deny (incoming)"), new("Logging", "on (low)") },
		});

		Assert.Contains("moka-data-list--row", html, StringComparison.Ordinal);
		Assert.Contains(">Policies<", html, StringComparison.Ordinal);
		Assert.True(html.IndexOf(">Default<", StringComparison.Ordinal) < html.IndexOf("deny (incoming)", StringComparison.Ordinal), "the label comes first");
	}

	private static int Count(string text, string part) => Regex.Count(text, Regex.Escape(part));

	private static Task<string> RenderComponentAsync<TComponent>(Dictionary<string, object?> parameters)
		where TComponent : IComponent =>
		StaticRender.RenderAsync<TComponent>(
			services =>
			{
				services.AddSingleton(TimeProvider.System);
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<ISettingsService>(new FakeSettingsService());
				services.AddSingleton<IUserInteraction>(new RecordingInteraction());
			},
			parameters)
			.WaitAsync(Wait, Ct);

	[GeneratedRegex("""<span class="moka-meter[^"]*"[^>]*aria-label="/srv/data"[^>]*>""")]
	private static partial Regex SrvDataMeter();
}
