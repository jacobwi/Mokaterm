using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Rdp.Components;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Extensions;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>The RDP options editor, rendered for real: the automatic sign-in switch exists only while network level authentication is off.</summary>
public sealed class RdpOptionsEditorRenderTests
{
	// The label goes on with an apostrophe, which the renderer writes as an entity.
	private const string AutologonLabel = "Sign in with this login";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task WithNlaOn_ThereIsNoAutologonSwitch()
	{
		string html = await RenderAsync(RdpConnectionOptions.Default);

		Assert.Contains("Network level authentication", html, StringComparison.Ordinal);
		Assert.DoesNotContain(AutologonLabel, html, StringComparison.Ordinal);
		Assert.DoesNotContain("encrypted inside TLS", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task WithNlaOff_TheAutologonSwitchSaysWhereThePasswordGoes()
	{
		string html = await RenderAsync(RdpConnectionOptions.Default with { NetworkLevelAuthentication = false });

		Assert.Contains(AutologonLabel, html, StringComparison.Ordinal);
		Assert.Contains(
			"The name and password go to the PC encrypted inside TLS, only after its certificate passed the check",
			html,
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task WithAutologonOff_SaysTheSignInScreenStaysUp()
	{
		string html = await RenderAsync(RdpConnectionOptions.Default with { NetworkLevelAuthentication = false, Autologon = false });

		Assert.Contains(AutologonLabel, html, StringComparison.Ordinal);
		Assert.Contains("Windows shows its own sign-in screen", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(RdpConnectionOptions options) =>
		StaticRender.RenderAsync<RdpOptionsEditor>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<ISettingsService>(new FakeSettingsService());
				services.AddSingleton<IUserInteraction, DismissingInteraction>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[nameof(ConnectionOptionsEditorBase.Options)] = options.ApplyTo(ProtocolOptions.Empty),
			})
			.WaitAsync(TimeSpan.FromSeconds(5), Ct);
}
