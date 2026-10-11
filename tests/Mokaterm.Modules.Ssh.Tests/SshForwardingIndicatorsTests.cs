using Mokaterm.Abstractions.Sessions;
using Mokaterm.Modules.Ssh.Forwarding;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>Agent and X11 forwarding as the status bar's chips: nothing while off, and a state and a sentence otherwise.</summary>
public sealed class SshForwardingIndicatorsTests
{
	[Fact]
	public void Off_ShowsNothing() =>
		Assert.Null(SshForwardingIndicators.Describe("AGENT", "Agent forwarding", SshForwardingLine.Off));

	[Fact]
	public void Requested_WithNothingRelayed_IsOn()
	{
		SessionIndicator? chip = SshForwardingIndicators.Describe(
			"AGENT",
			"Agent forwarding",
			new SshForwardingLine(SshForwardingState.Requested, "Your SSH agent", 0, null));

		Assert.NotNull(chip);
		Assert.Equal(SessionIndicatorState.On, chip.State);
		Assert.Equal("Agent forwarding to Your SSH agent is on. Programs on the server can use it.", chip.Description);
	}

	[Fact]
	public void Requested_WithChannelsOpen_IsActive_AndCountsThem()
	{
		SessionIndicator? chip = SshForwardingIndicators.Describe(
			"X11",
			"X11 forwarding",
			new SshForwardingLine(SshForwardingState.Requested, "localhost:0", 2, null));

		Assert.NotNull(chip);
		Assert.Equal(SessionIndicatorState.Active, chip.State);
		Assert.Contains("2 connections", chip.Description, StringComparison.Ordinal);
	}

	[Fact]
	public void Unavailable_IsAProblem_WithTheReason()
	{
		SessionIndicator? chip = SshForwardingIndicators.Describe(
			"X11",
			"X11 forwarding",
			new SshForwardingLine(SshForwardingState.Unavailable, "localhost:0", 0, "no X server answers on display 0"));

		Assert.NotNull(chip);
		Assert.Equal(SessionIndicatorState.Problem, chip.State);
		Assert.EndsWith("no X server answers on display 0", chip.Description, StringComparison.Ordinal);
	}

	[Fact]
	public void Indicators_ListAgentThenX11_AndPassChangesOn()
	{
		FakeForwarding forwarding = new()
		{
			Agent = new SshForwardingLine(SshForwardingState.Requested, "Your SSH agent", 0, null),
			X11 = new SshForwardingLine(SshForwardingState.Requested, "localhost:0", 1, null),
		};
		SshForwardingIndicators indicators = new(forwarding);
		int changes = 0;
		indicators.Changed += () => changes++;

		forwarding.Raise();

		Assert.Equal(["AGENT", "X11"], indicators.Indicators.Select(chip => chip.Label));
		Assert.Equal(1, changes);
	}

	private sealed class FakeForwarding : ISshForwardingFeature
	{
		public event Action? Changed;

		public SshForwardingLine Agent { get; init; } = SshForwardingLine.Off;

		public SshForwardingLine X11 { get; init; } = SshForwardingLine.Off;

		public void Raise() => Changed?.Invoke();
	}
}
