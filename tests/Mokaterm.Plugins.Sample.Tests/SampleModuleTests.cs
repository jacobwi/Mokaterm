using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.Plugins.Sample.Fail2ban;
using Mokaterm.Plugins.Sample.Subnets;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Editing;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.Plugins.Sample.Tests;

/// <summary>What the sample registers, through the same public descriptors the built-in pages use.</summary>
public sealed class SampleModuleTests
{
	[Fact]
	public void TheFail2banPage_SitsBeforeSecurity_AndHasDetectionLookForItsClient()
	{
		IUiContributions contributions = Contributions();

		SystemPageDescriptor page = Assert.Single(contributions.SystemPages);
		Assert.Equal("fail2ban", page.Id);
		Assert.Equal(typeof(Fail2banPage), page.Component);
		Assert.True(typeof(SystemPageBase).IsAssignableFrom(page.Component));
		Assert.InRange(page.Order, 1101, 1199);
		Assert.Equal([Fail2banCommands.Client], page.Commands);
	}

	[Fact]
	public void TheFail2banPage_IsGreyedWhereTheClientIsMissing()
	{
		SystemPageDescriptor page = Assert.Single(Contributions().SystemPages);
		Func<ISystemPageContext, string?> rule = page.Unavailable ?? throw new InvalidOperationException("The page has no rule.");

		Assert.NotNull(rule(new Facts(new ServerFacts { DisplayName = "Debian 12" })));
		Assert.Null(rule(new Facts(new ServerFacts
		{
			DisplayName = "Debian 12",
			Commands = new HashSet<string>(StringComparer.Ordinal) { Fail2banCommands.Client },
		})));
	}

	[Fact]
	public void TheSubnetCalculator_IsAnAppTool()
	{
		AppToolDescriptor tool = Assert.Single(Contributions().AppTools);

		Assert.Equal("subnet-calculator", tool.Id);
		Assert.Equal(typeof(SubnetCalculatorTool), tool.Component);
		Assert.True(typeof(AppToolBase).IsAssignableFrom(tool.Component));
		Assert.Equal(SessionToolSize.Standard, tool.Size);
		Assert.False(string.IsNullOrWhiteSpace(tool.Description));
	}

	[Fact]
	public void TheSessionTool_IsStillThere()
	{
		IUiContributions contributions = Contributions();

		Assert.Single(contributions.FindSessionTools("demo"));
		Assert.Single(contributions.FindSessionTools("local"));
	}

	[Fact]
	public void TheSettingsPage_SitsInThePluginsGroup_UnderThePluginsOwnName()
	{
		SettingsPageDescriptor page = Assert.Single(Contributions().SettingsPages);

		Assert.Equal(SampleSettings.SectionKey, page.Id);
		Assert.Equal(SettingsPageGroup.Plugins, page.Group);
		Assert.Equal(typeof(SampleSettingsPage), page.Component);
		Assert.StartsWith("plugin.moka.sample", SampleSettings.SectionKey, StringComparison.Ordinal);
	}

	private static IUiContributions Contributions()
	{
		ServiceCollection services = new();
		new SampleModule().ConfigureServices(services);
		using ServiceProvider provider = services.BuildServiceProvider();
		return provider.GetRequiredService<IUiContributions>();
	}

	/// <summary>A System view that only knows the server's facts, which is all a rule may read.</summary>
	private sealed class Facts(ServerFacts server) : ISystemPageContext
	{
		public ServerFacts Server { get; } = server;

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

		public Task<SystemScriptResult> ReadAsync(SystemScript script, CancellationToken cancellationToken) =>
			throw new InvalidOperationException("A rule never reads the server.");

		public Task<SystemChangeResult?> RunChangeAsync(SystemChange change) =>
			throw new InvalidOperationException("A rule never changes the server.");

		public Task<RemoteTextEditResult> EditFileAsync(string path, bool asRoot, bool createNew, CancellationToken cancellationToken) =>
			throw new InvalidOperationException("A rule never opens a file.");
	}
}
