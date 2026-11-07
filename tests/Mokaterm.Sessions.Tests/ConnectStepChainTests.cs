using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Sessions.Tests;

/// <summary>
/// The rules every step report follows, which the session handle applies under its lock: a step that starts closes
/// the ones before it and reopens the ones after it, a failure marks one step, and the chain is replaced only when a
/// report changed something.
/// </summary>
public sealed class ConnectStepChainTests
{
	private static readonly ConnectStep[] Plan =
	[
		new("dial", "Connect", ConnectStepKind.Dial),
		new("key", "Host key", ConnectStepKind.HostIdentity),
		new("login", "Login", ConnectStepKind.Login),
		new("shell", "Shell", ConnectStepKind.Session),
	];

	[Fact]
	public void Of_StartsEveryStepPending_InPlanOrder()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan);

		Assert.Equal(Plan, chain.Steps.Select(status => status.Step));
		Assert.All(chain.Steps, status => Assert.Equal(ConnectStepState.Pending, status.State));
		Assert.Null(chain.Current);
		Assert.Null(chain.Failed);
		Assert.False(chain.IsEmpty);
	}

	[Fact]
	public void Of_NoSteps_IsTheEmptyChain() => Assert.Same(ConnectStepChain.Empty, ConnectStepChain.Of([]));

	[Theory]
	[InlineData("dial", "dial")]
	[InlineData("", "key")]
	[InlineData(" ", "key")]
	public void Of_RefusesAMissingOrRepeatedId(string first, string second) =>
		Assert.Throws<ArgumentException>(() => ConnectStepChain.Of([new ConnectStep(first, "A"), new ConnectStep(second, "B")]));

	[Fact]
	public void Start_ClosesTheStepsBefore_AndLeavesTheRestPending()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Start("login", "publickey");

		Assert.Equal([ConnectStepState.Done, ConnectStepState.Done, ConnectStepState.Running, ConnectStepState.Pending], States(chain));
		Assert.Equal("login", chain.Current?.Step.Id);
		Assert.Equal("publickey", chain.Current?.Detail);
	}

	[Fact]
	public void Start_AnEarlierStep_ReopensEverythingAfterIt()
	{
		// A second dial after the user approved a host key starts the chain over from the dial.
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Start("login").Fail("login", "refused").Start("dial");

		Assert.Equal([ConnectStepState.Running, ConnectStepState.Pending, ConnectStepState.Pending, ConnectStepState.Pending], States(chain));
		Assert.All(chain.Steps.Skip(1), status => Assert.Null(status.Detail));
	}

	[Fact]
	public void Start_TheRunningStepAgain_OnlyReplacesItsDetail()
	{
		ConnectStepChain first = ConnectStepChain.Of(Plan).Start("dial", "192.0.2.1");

		ConnectStepChain second = first.Start("dial", "192.0.2.2");

		Assert.Equal("192.0.2.2", second.Current?.Detail);
		Assert.Same(second, second.Start("dial", "192.0.2.2"));
	}

	[Fact]
	public void Start_KeepsASkippedStepSkipped()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Start("key").Skip("login", "not asked").Start("shell");

		Assert.Equal([ConnectStepState.Done, ConnectStepState.Done, ConnectStepState.Skipped, ConnectStepState.Running], States(chain));
		Assert.Equal("not asked", chain.Steps[2].Detail);
	}

	[Fact]
	public void Complete_ClosesTheStepAndEveryOneBefore_ButNotTheOnesAfter()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Complete("key");

		Assert.Equal([ConnectStepState.Done, ConnectStepState.Done, ConnectStepState.Pending, ConnectStepState.Pending], States(chain));
	}

	[Fact]
	public void Fail_KeepsAStepNeverReachedPending_AndReopensTheOnesAfter()
	{
		// A key that cannot be read fails the login before anything was dialed.
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Fail("login", "No private key is saved for this login.");

		Assert.Equal([ConnectStepState.Pending, ConnectStepState.Pending, ConnectStepState.Failed, ConnectStepState.Pending], States(chain));
		Assert.Equal("No private key is saved for this login.", chain.Failed?.Detail);
	}

	[Fact]
	public void Fail_ClosesAStepBeforeItThatWasStillRunning()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Start("key").Fail("login", "refused");

		Assert.Equal([ConnectStepState.Done, ConnectStepState.Done, ConnectStepState.Failed, ConnectStepState.Pending], States(chain));
	}

	[Fact]
	public void AReportForAStepNotPlanned_ChangesNothing()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Start("dial");

		Assert.Same(chain, chain.Start("open"));
		Assert.Same(chain, chain.Fail("open", "no"));
		Assert.Same(chain, chain.Skip("open"));
		Assert.Same(chain, chain.Complete("open"));
	}

	[Fact]
	public void AReportThatChangesNothing_ReturnsTheSameChain()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Start("key");

		Assert.Same(chain, chain.Start("key"));
		Assert.Same(chain, chain.Complete("dial"));
	}

	[Fact]
	public void FailCurrent_FailsTheStepUnderWay()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Start("key").FailCurrent("Timed out connecting to host:22.");

		Assert.Equal([ConnectStepState.Done, ConnectStepState.Failed, ConnectStepState.Pending, ConnectStepState.Pending], States(chain));
		Assert.Equal("Timed out connecting to host:22.", chain.Failed?.Detail);
	}

	[Fact]
	public void FailCurrent_WithNothingUnderWay_FailsTheFirstStepNeverReached()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Complete("key").FailCurrent("The vault is locked.");

		Assert.Equal("login", chain.Failed?.Step.Id);
	}

	[Fact]
	public void FailCurrent_AfterEveryStepHeld_FailsTheLastOneThatApplied()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Skip("shell").Complete("login").FailCurrent("The terminal could not be opened.");

		Assert.Equal("login", chain.Failed?.Step.Id);
		Assert.Equal(ConnectStepState.Skipped, chain.Steps[3].State);
	}

	[Fact]
	public void FailCurrent_KeepsAFailureTheProviderReported()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Fail("login", "No password was provided.");

		Assert.Same(chain, chain.FailCurrent("The login was cancelled."));
	}

	[Fact]
	public void FailCurrent_OnAnEmptyChain_StaysEmpty() =>
		Assert.Same(ConnectStepChain.Empty, ConnectStepChain.Empty.FailCurrent("No route to host."));

	[Fact]
	public void CompleteAll_ClosesEveryStepThatApplied()
	{
		ConnectStepChain chain = ConnectStepChain.Of(Plan).Skip("key").Start("login").CompleteAll();

		Assert.Equal([ConnectStepState.Done, ConnectStepState.Skipped, ConnectStepState.Done, ConnectStepState.Done], States(chain));
	}

	private static ConnectStepState[] States(ConnectStepChain chain) => [.. chain.Steps.Select(status => status.State)];
}
