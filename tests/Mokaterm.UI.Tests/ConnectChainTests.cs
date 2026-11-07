using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Core.Icons;
using Moka.Red.Extensions;
using Moka.Red.Primitives.ChainCheck;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The chain a session tab draws from its connect steps: what each step state looks like, where the failure and its
/// fix go, and that a step the attempt never reached reads as off once it failed.
/// </summary>
public sealed class ConnectChainTests
{
	private static readonly MokaIconDefinition SessionIcon = MokatermIcons.Terminal;

	private static readonly ConnectStep[] Plan =
	[
		new("dial", "Connect", ConnectStepKind.Dial),
		new("key", "Host key", ConnectStepKind.HostIdentity),
		new("login", "Login", ConnectStepKind.Login),
		new("shell", "Shell", ConnectStepKind.Session),
	];

	[Fact]
	public void Connecting_DrawsTheStepsThatHeld_TheOneUnderWay_AndTheOnesToCome()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Start("key");

		IReadOnlyList<MokaChainStep> steps = ConnectChainDisplay.Steps(chain, failed: false, null, null, SessionIcon, fix: null);

		Assert.Equal([MokaChainTone.Ok, MokaChainTone.Wait, MokaChainTone.Wait, MokaChainTone.Wait], steps.Select(step => step.Tone));
		Assert.Equal(["", ConnectChainDisplay.RunningWord, "", ""], steps.Select(step => step.Word));
		Assert.All(steps, step => Assert.Equal("", step.Sentence));
		Assert.Equal(["Connect", "Host key", "Login", "Shell"], steps.Select(step => step.Label));
	}

	[Fact]
	public void AFailure_GoesUnderTheStepThatBroke_WithItsFix_AndTheRestIsOff()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Start("key").FailCurrent("Timed out connecting to build-01:22.");
		RenderFragment fix = _ => { };

		IReadOnlyList<MokaChainStep> steps = ConnectChainDisplay.Steps(
			chain,
			failed: true,
			ConnectFailure.Timeout,
			"Timed out connecting to build-01:22.",
			SessionIcon,
			fix);

		Assert.Equal([MokaChainTone.Ok, MokaChainTone.Fail, MokaChainTone.Off, MokaChainTone.Off], steps.Select(step => step.Tone));
		MokaChainStep failed = steps[1];
		Assert.Equal("Timed out", failed.Word);
		Assert.Equal("Timed out connecting to build-01:22.", failed.Sentence);
		Assert.Same(fix, failed.Fix);
		Assert.All(steps.Where(step => step != failed), step => Assert.Null(step.Fix));
	}

	[Fact]
	public void AStepNeverReachedBeforeTheFailure_IsOffToo()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Fail("login", "No private key is saved for this login.");

		IReadOnlyList<MokaChainStep> steps = ConnectChainDisplay.Steps(
			chain,
			failed: true,
			ConnectFailure.AuthenticationFailed,
			"No private key is saved for this login.",
			SessionIcon,
			fix: null);

		Assert.Equal([MokaChainTone.Off, MokaChainTone.Off, MokaChainTone.Fail, MokaChainTone.Off], steps.Select(step => step.Tone));
		Assert.Equal("Refused", steps[2].Word);
	}

	[Fact]
	public void ARefusalWhileStillConnecting_SaysWhatTheProviderReported_WithoutAFix()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Start("login").Fail("login", "The server rejected the login.");

		IReadOnlyList<MokaChainStep> steps = ConnectChainDisplay.Steps(chain, failed: false, null, "Waiting for credentials", SessionIcon, _ => { });

		Assert.Equal(MokaChainTone.Fail, steps[2].Tone);
		Assert.Equal("The server rejected the login.", steps[2].Sentence);
		Assert.Null(steps[2].Fix);
		Assert.Equal(MokaChainTone.Off, steps[3].Tone);
	}

	[Fact]
	public void ASkippedStep_IsOff_AndSaysItWasNotUsed()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Skip("login", "TLS only").Start("shell");

		MokaChainStep skipped = ConnectChainDisplay.Steps(chain, failed: false, null, null, SessionIcon, fix: null)[2];

		Assert.Equal(MokaChainTone.Off, skipped.Tone);
		Assert.Equal(ConnectChainDisplay.SkippedWord, skipped.Word);
		Assert.Equal("", skipped.Sentence);
	}

	[Fact]
	public void TheStepThatOpensTheSession_TakesTheProtocolsIcon_AndEveryKindHasOne()
	{
		Assert.Equal(SessionIcon, ConnectChainDisplay.IconFor(ConnectStepKind.Session, SessionIcon));
		Assert.All(
			Enum.GetValues<ConnectStepKind>(),
			kind => Assert.False(string.IsNullOrEmpty(ConnectChainDisplay.IconFor(kind, SessionIcon).SvgPath)));
	}

	[Fact]
	public async Task TheChain_RendersEveryStep_TheFailure_AndTheFix()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Start("login").FailCurrent("The server rejected the login.");
		FakeSession session = new FakeSession { Failure = ConnectFailure.AuthenticationFailed, ConnectSteps = chain }
			.Become(SessionState.Failed, "The server rejected the login.");
		RenderFragment fix = builder => builder.AddContent(0, "Edit connection");

		string html = await StaticRender.RenderAsync<ConnectChain>(
			services =>
			{
				services.AddMokaRed();
				services.AddSingleton<IUiContributions>(new UiContributions([], [], []));
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[nameof(ConnectChain.Session)] = session,
				[nameof(ConnectChain.Chain)] = chain,
				[nameof(ConnectChain.Fix)] = fix,
			}).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

		Assert.Contains("moka-chain-check__step--fail", html, StringComparison.Ordinal);
		Assert.Contains("Host key", html, StringComparison.Ordinal);
		Assert.Contains("Refused", html, StringComparison.Ordinal);
		Assert.Contains("The server rejected the login.", html, StringComparison.Ordinal);
		Assert.Contains("Edit connection", html, StringComparison.Ordinal);
	}
}
