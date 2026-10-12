using Mokaterm.Abstractions.Commands;
using Mokaterm.Modules.Ssh.Shell;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>The per-connection cap on exec channels: who waits, who goes first, and that a long call cannot starve a short one.</summary>
public sealed class ExecChannelBudgetTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task UpToTheCapacity_GoesAtOnce_ThenCallsWaitInTurn()
	{
		ExecChannelBudget budget = new(capacity: 2, reservedForShort: 0);
		ExecSlot first = await budget.AcquireAsync(runsLong: false, Ct);
		ExecSlot second = await budget.AcquireAsync(runsLong: false, Ct);

		Task<ExecSlot> third = budget.AcquireAsync(runsLong: false, Ct).AsTask();
		Task<ExecSlot> fourth = budget.AcquireAsync(runsLong: false, Ct).AsTask();

		Assert.False(third.IsCompleted);
		Assert.Equal(2, budget.Waiting);

		first.Dispose();
		ExecSlot thirdSlot = await third.WaitAsync(Wait, Ct);

		Assert.False(fourth.IsCompleted);
		second.Dispose();
		ExecSlot fourthSlot = await fourth.WaitAsync(Wait, Ct);

		thirdSlot.Dispose();
		fourthSlot.Dispose();
		Assert.Equal(0, budget.InUse);
	}

	[Fact]
	public async Task LongCalls_NeverTakeTheSlotsKeptForShortOnes()
	{
		ExecChannelBudget budget = new(capacity: 6, reservedForShort: 2);
		List<ExecSlot> installs = [];
		for (int index = 0; index < 4; index++)
		{
			installs.Add(await budget.AcquireAsync(runsLong: true, Ct));
		}

		Task<ExecSlot> fifthInstall = budget.AcquireAsync(runsLong: true, Ct).AsTask();
		ExecSlot read = await budget.AcquireAsync(runsLong: false, Ct).AsTask().WaitAsync(Wait, Ct);
		ExecSlot otherRead = await budget.AcquireAsync(runsLong: false, Ct).AsTask().WaitAsync(Wait, Ct);

		Assert.False(fifthInstall.IsCompleted);
		Assert.Equal(6, budget.InUse);

		// The budget is full now. A read that comes free goes to the next read, not to the install queued before it.
		Task<ExecSlot> laterRead = budget.AcquireAsync(runsLong: false, Ct).AsTask();
		read.Dispose();
		ExecSlot laterSlot = await laterRead.WaitAsync(Wait, Ct);

		Assert.False(fifthInstall.IsCompleted);

		// A finished install makes room in the long share, and the queued install takes it before any later call.
		installs[0].Dispose();
		ExecSlot fifth = await fifthInstall.WaitAsync(Wait, Ct);

		foreach (ExecSlot slot in (ExecSlot[])[.. installs.Skip(1), fifth, otherRead, laterSlot])
		{
			slot.Dispose();
		}

		Assert.Equal(0, budget.InUse);
	}

	[Fact]
	public async Task AQueuedLongCall_GoesBeforeALaterOne_OnceTheBudgetHasRoom()
	{
		ExecChannelBudget budget = new(capacity: 2, reservedForShort: 0);
		ExecSlot a = await budget.AcquireAsync(runsLong: false, Ct);
		ExecSlot b = await budget.AcquireAsync(runsLong: false, Ct);
		Task<ExecSlot> install = budget.AcquireAsync(runsLong: true, Ct).AsTask();
		Task<ExecSlot> read = budget.AcquireAsync(runsLong: false, Ct).AsTask();

		a.Dispose();
		ExecSlot installSlot = await install.WaitAsync(Wait, Ct);

		Assert.False(read.IsCompleted);
		b.Dispose();
		(await read.WaitAsync(Wait, Ct)).Dispose();
		installSlot.Dispose();
	}

	[Fact]
	public async Task ACancelledWait_TakesNoSlot_AndLeavesTheQueue()
	{
		ExecChannelBudget budget = new(capacity: 1, reservedForShort: 0);
		ExecSlot held = await budget.AcquireAsync(runsLong: false, Ct);
		using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
		Task<ExecSlot> abandoned = budget.AcquireAsync(runsLong: false, cancel.Token).AsTask();
		Task<ExecSlot> next = budget.AcquireAsync(runsLong: false, Ct).AsTask();

		await cancel.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned.WaitAsync(Wait, Ct));
		Assert.Equal(1, budget.Waiting);

		held.Dispose();
		(await next.WaitAsync(Wait, Ct)).Dispose();
		Assert.Equal(0, budget.InUse);
		Assert.Equal(0, budget.Waiting);
	}

	[Fact]
	public async Task AnAlreadyCancelledCall_IsRefused_EvenWithRoom()
	{
		ExecChannelBudget budget = new();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => budget.AcquireAsync(runsLong: false, new CancellationToken(canceled: true)).AsTask());
		Assert.Equal(0, budget.InUse);
	}

	[Fact]
	public async Task ASlot_IsGivenBackOnce()
	{
		ExecChannelBudget budget = new(capacity: 2, reservedForShort: 0);
		ExecSlot slot = await budget.AcquireAsync(runsLong: false, Ct);
		ExecSlot other = await budget.AcquireAsync(runsLong: false, Ct);

		slot.Dispose();
		slot.Dispose();

		Assert.Equal(1, budget.InUse);
		other.Dispose();
	}

	[Fact]
	public void EachConnection_HasABudgetOfItsOwn()
	{
		using SshClient one = new("127.0.0.1", "tester", "secret");
		using SshClient two = new("127.0.0.1", "tester", "secret");

		Assert.Same(ExecChannelBudget.For(one), ExecChannelBudget.For(one));
		Assert.NotSame(ExecChannelBudget.For(one), ExecChannelBudget.For(two));
	}

	[Theory]
	[InlineData(30, false)]
	[InlineData(180, false)]
	[InlineData(300, false)]
	[InlineData(20 * 60, true)]
	[InlineData(30 * 60, true)]
	public void RunsLong_FollowsTheTimeLimit(int seconds, bool expected) =>
		Assert.Equal(expected, ExecChannelBudget.RunsLong(TimeSpan.FromSeconds(seconds)));

	[Fact]
	public async Task ACommand_WaitsForItsConnectionsBudget_AndGivesTheSlotBackWhenItCannotStart()
	{
		// Never connected: once the command gets a slot, SSH.NET refuses to open the channel, which is enough to see
		// that the slot is taken first and given back after.
		using SshClient client = new("127.0.0.1", "tester", "secret");
		ExecChannelBudget budget = ExecChannelBudget.For(client);
		SshCommandFeature commands = new(_ => Task.FromResult(client), TimeProvider.System);
		List<ExecSlot> held = [];
		for (int index = 0; index < ExecChannelBudget.Capacity; index++)
		{
			held.Add(await budget.AcquireAsync(runsLong: false, Ct));
		}

		using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
		Task<RemoteCommandOutcome> waiting = commands.RunAsync(new RemoteCommandRequest { Command = "uptime" }, cancel.Token);
		await Eventually.TrueAsync(() => budget.Waiting == 1, "the command waits for a slot");
		await cancel.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(Wait, Ct));
		Assert.Equal(0, budget.Waiting);

		Task<RemoteCommandOutcome> next = commands.RunAsync(new RemoteCommandRequest { Command = "uptime" }, Ct);
		await Eventually.TrueAsync(() => budget.Waiting == 1, "the next command waits for a slot");
		held[0].Dispose();

		await Assert.ThrowsAsync<RemoteCommandException>(() => next.WaitAsync(Wait, Ct));
		Assert.Equal(ExecChannelBudget.Capacity - 1, budget.InUse);

		foreach (ExecSlot slot in held.Skip(1))
		{
			slot.Dispose();
		}
	}
}
