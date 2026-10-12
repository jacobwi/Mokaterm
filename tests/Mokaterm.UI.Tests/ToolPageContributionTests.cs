using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.UI.Tests;

/// <summary>
/// System pages, Docker pages and app tools come from descriptors, the built-in ones too: they are checked when they are
/// registered, listed in order, one per id, and a page's availability rule cannot take its tool down.
/// </summary>
public sealed class ToolPageContributionTests
{
	[Fact]
	public void SystemPages_AreListedByOrderThenTitle_WhicheverWasRegisteredFirst()
	{
		IUiContributions contributions = Build(services => services
			.AddSystemPage(SystemPage("security", "Security", 1200))
			.AddSystemPage(SystemPage("overview", "Overview", 0))
			.AddSystemPage(SystemPage("fail2ban", "Fail2ban", 1150))
			.AddSystemPage(SystemPage("cron", "Cron", 1150)));

		Assert.Equal(["overview", "cron", "fail2ban", "security"], contributions.SystemPages.Select(page => page.Id));
	}

	[Fact]
	public void ALaterPageWithTheSameId_TakesThePlaceOfTheFirst()
	{
		IUiContributions contributions = Build(services => services
			.AddSystemPage(SystemPage("services", "Services", 100))
			.AddSystemPage(SystemPage("services", "Better services", 100))
			.AddDockerPage(DockerPage("images", "Images", 400))
			.AddDockerPage(DockerPage("images", "Registry images", 400)));

		Assert.Equal("Better services", Assert.Single(contributions.SystemPages).Title);
		Assert.Equal("Registry images", Assert.Single(contributions.DockerPages).Title);
	}

	[Fact]
	public void NothingRegistered_MeansEmptyLists()
	{
		IUiContributions contributions = Build(services => services.AddSessionTool(new SessionToolDescriptor
		{
			ProtocolId = "ssh",
			Title = "Tool",
			Icon = MokatermIcons.Tools,
			Component = typeof(NoSessionView),
		}));

		Assert.Empty(contributions.SystemPages);
		Assert.Empty(contributions.DockerPages);
		Assert.Empty(contributions.AppTools);
	}

	[Fact]
	public void APage_MustDeriveFromItsToolsBase()
	{
		ServiceCollection services = new();

		Assert.Throws<ArgumentException>(() => services.AddSystemPage(SystemPage("x", "X", 0) with { Component = typeof(NoDockerPage) }));
		Assert.Throws<ArgumentException>(() => services.AddDockerPage(DockerPage("x", "X", 0) with { Component = typeof(NoSystemPage) }));
		Assert.Throws<ArgumentException>(() => services.AddAppTool(AppTool("x", "X", 0) with { Component = typeof(NoSystemPage) }));
		Assert.Empty(services);
	}

	[Theory]
	[InlineData("")]
	[InlineData("Services")]
	[InlineData("two words")]
	[InlineData("-leading")]
	[InlineData("line\n")]
	[InlineData("dots.are.not.ids")]
	public void AnIdThatIsNotLowerCaseWordsWithDashes_IsRefused(string id)
	{
		ServiceCollection services = new();

		Assert.False(UiContributionServiceCollectionExtensions.IsToolId(id));
		Assert.Throws<ArgumentException>(() => services.AddSystemPage(SystemPage(id, "X", 0)));
		Assert.Throws<ArgumentException>(() => services.AddAppTool(AppTool(id, "X", 0)));
	}

	[Theory]
	[InlineData("fail2ban; rm -rf /")]
	[InlineData("$(id)")]
	[InlineData("two words")]
	[InlineData("name\n")]
	[InlineData("")]
	public void ACommandToProbeThatIsNotAPlainProgramName_IsRefused(string command)
	{
		ServiceCollection services = new();

		Assert.Throws<ArgumentException>(() => services.AddSystemPage(SystemPage("fail2ban", "Fail2ban", 0) with { Commands = [command] }));
	}

	[Fact]
	public void AppTools_AreListedByOrderThenTitle()
	{
		IUiContributions contributions = Build(services => services
			.AddAppTool(AppTool("subnet-calculator", "Subnet calculator", 10))
			.AddAppTool(AppTool("jwt", "JWT decoder", 10))
			.AddAppTool(AppTool("base64", "Base64", 0)));

		Assert.Equal(["base64", "jwt", "subnet-calculator"], contributions.AppTools.Select(tool => tool.Id));
	}

	[Fact]
	public void ARule_AnswersForThePage_AndNoRuleMeansUsable()
	{
		Assert.Null(ToolPageRules.Evaluate<string>(null, "context", NullLogger.Instance, "page"));
		Assert.Equal("Not here.", ToolPageRules.Evaluate(context => context == "server" ? "Not here." : null, "server", NullLogger.Instance, "page"));
		Assert.Null(ToolPageRules.Evaluate(context => context == "server" ? "Not here." : null, "other", NullLogger.Instance, "page"));
	}

	[Fact]
	public void ARuleThatThrows_GreysItsOwnPageInsteadOfTakingTheToolDown()
	{
		string? reason = ToolPageRules.Evaluate<string>(_ => throw new InvalidOperationException("plugin bug"), "server", NullLogger.Instance, "fail2ban");

		Assert.Equal(ToolPageRules.RuleFailed, reason);
	}

	private static IUiContributions Build(Action<IServiceCollection> configure)
	{
		ServiceCollection services = new();
		configure(services);
		using ServiceProvider provider = services.BuildServiceProvider();
		return provider.GetRequiredService<IUiContributions>();
	}

	private static SystemPageDescriptor SystemPage(string id, string title, int order) => new()
	{
		Id = id,
		Title = title,
		Icon = MokatermIcons.Server,
		Component = typeof(NoSystemPage),
		Order = order,
	};

	private static DockerPageDescriptor DockerPage(string id, string title, int order) => new()
	{
		Id = id,
		Title = title,
		Icon = MokatermIcons.Server,
		Component = typeof(NoDockerPage),
		Order = order,
	};

	private static AppToolDescriptor AppTool(string id, string title, int order) => new()
	{
		Id = id,
		Title = title,
		Icon = MokatermIcons.Tools,
		Component = typeof(NoAppTool),
		Order = order,
	};

	/// <summary>A page these tests never render.</summary>
	private sealed class NoSystemPage : SystemPageBase
	{
		protected override Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	private sealed class NoDockerPage : DockerPageBase;

	private sealed class NoAppTool : AppToolBase;

	private sealed class NoSessionView : Mokaterm.UI.Common.Components.SessionViewBase;
}
