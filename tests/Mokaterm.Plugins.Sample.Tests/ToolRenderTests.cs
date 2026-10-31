using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Plugin.TestHost;
using Mokaterm.Plugins.Sample.Subnets;

namespace Mokaterm.Plugins.Sample.Tests;

/// <summary>
/// The subnet calculator drawn through the render harness a plugin's tests get as a package: what the tool shows on
/// each host, without a browser. The harness is what the other first-party plugins' tests will use too.
/// </summary>
public sealed class ToolRenderTests
{
	[Theory]
	[InlineData(HostKind.Desktop)]
	[InlineData(HostKind.Web)]
	public async Task SubnetCalculator_OnEitherHost_ShowsItsFields(HostKind host)
	{
		string html = await StaticRender.RenderAsync<SubnetCalculatorTool>(new Dictionary<string, object?>(StringComparer.Ordinal), host);

		Assert.Contains("Address and prefix", html, StringComparison.Ordinal);
		Assert.Contains("192.168.1.10/24", html, StringComparison.Ordinal);
		Assert.Contains("data-autofocus", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ATestThatSteersTheServices_UsesWhatItGave()
	{
		// The same fakes the harness registers by default, seeded the way a test wants them.
		TestSettings settings = new();
		TestInteraction interaction = new();
		string html = await StaticRender.RenderAsync<SubnetCalculatorTool>(
			services =>
			{
				services.AddSingleton<ISettingsService>(settings);
				services.AddSingleton<IUserInteraction>(interaction);
			},
			new Dictionary<string, object?>(StringComparer.Ordinal));

		Assert.Contains("Address and prefix", html, StringComparison.Ordinal);
		Assert.Empty(interaction.TakeNotices());
	}
}
