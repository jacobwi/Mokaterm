using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Local.Components;
using Mokaterm.Modules.Local.Containers;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Extensions;

namespace Mokaterm.Modules.Local.Tests;

/// <summary>The settings page and the options editors, rendered for real against this computer's shells.</summary>
public sealed class LocalComponentRenderTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task SettingsPage_ShowsEverySectionAndWhatIsStored()
	{
		FakeSettingsService stored = new();
		stored.Set(new LocalSettings { StartingFolder = @"D:\work", ReportExitCode = false });

		string html = await RenderAsync<LocalSettingsPage>(stored, new());

		Assert.Contains("What a login runs", html, StringComparison.Ordinal);
		Assert.Contains("When a shell exits", html, StringComparison.Ordinal);
		Assert.Contains("Found on this computer", html, StringComparison.Ordinal);
		Assert.Contains("value=\"D:\\work\"", html, StringComparison.Ordinal);
		Assert.Contains("Reset to defaults", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task OptionsEditor_ForACustomProgram_AsksForTheProgram()
	{
		ProtocolOptions options = new LocalConnectionOptions { Shell = "custom", Program = "nu.exe" }.ApplyTo(ProtocolOptions.Empty);

		string html = await RenderAsync<LocalOptionsEditor>(new FakeSettingsService(), new() { [nameof(ConnectionOptionsEditorBase.Options)] = options });

		Assert.Contains("name of a program on PATH", html, StringComparison.Ordinal);
		Assert.Contains("value=\"nu.exe\"", html, StringComparison.Ordinal);
		Assert.Contains("Environment variables", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task OptionsEditor_ForAWslDistribution_TalksAboutLinux()
	{
		ProtocolOptions options = new LocalConnectionOptions { Shell = "wsl:NotInstalledAnywhere" }.ApplyTo(ProtocolOptions.Empty);

		string html = await RenderAsync<LocalOptionsEditor>(new FakeSettingsService(), new() { [nameof(ConnectionOptionsEditorBase.Options)] = options });

		Assert.DoesNotContain("name of a program on PATH", html, StringComparison.Ordinal);
		Assert.Contains("WSLENV", html, StringComparison.Ordinal);
		Assert.Contains("Not found on this computer", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DockerEditor_ShowsWhatTheLoginHas_AndWhatIsWrongWithIt()
	{
		ProtocolOptions options = new DockerShellOptions
		{
			Container = "shop-api-1",
			Context = "remote",
			Host = "ssh://ada@build",
			User = "root user",
			Shell = ContainerShell.Custom,
		}.ApplyTo(ProtocolOptions.Empty);

		string html = await RenderAsync<DockerShellOptionsEditor>(new FakeSettingsService(), new() { [nameof(ConnectionOptionsEditorBase.Options)] = options });

		Assert.Contains("value=\"shop-api-1\"", html, StringComparison.Ordinal);
		Assert.Contains("Choose", html, StringComparison.Ordinal);
		Assert.Contains("Name a context or a host, not both.", html, StringComparison.Ordinal);
		Assert.Contains("A user is a name or a uid", html, StringComparison.Ordinal);
		Assert.Contains("Name the command to run.", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task KubernetesEditor_ShowsWhatTheLoginHas_AndWhatIsWrongWithIt()
	{
		ProtocolOptions options = new KubernetesShellOptions { Pod = "Web-1", Namespace = "shop", Shell = ContainerShell.Bash }.ApplyTo(ProtocolOptions.Empty);

		string html = await RenderAsync<KubernetesShellOptionsEditor>(new FakeSettingsService(), new() { [nameof(ConnectionOptionsEditorBase.Options)] = options });

		Assert.Contains("value=\"shop\"", html, StringComparison.Ordinal);
		Assert.Contains("A pod is named with lower-case letters", html, StringComparison.Ordinal);
		Assert.Contains("which the image has to have", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Name the command to run.", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync<TComponent>(FakeSettingsService settings, Dictionary<string, object?> parameters)
		where TComponent : Microsoft.AspNetCore.Components.IComponent =>
		StaticRender.RenderAsync<TComponent>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<ISettingsService>(settings);
				services.AddSingleton<IUserInteraction, DismissingInteraction>();
			},
			parameters)
			.WaitAsync(TimeSpan.FromSeconds(5), Ct);
}
