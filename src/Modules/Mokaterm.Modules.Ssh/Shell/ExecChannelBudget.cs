using System.Runtime.CompilerServices;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// How many exec channels one SSH connection may have open at once. Everything in this module that opens one (the
/// Docker manager, the system view, sudo, command runs, the file browser's scripts) goes through
/// <see cref="RemoteCommand.StartAsync"/>, which takes a slot here first and gives it back when the channel is
/// disposed, so no tool can open channels past what the others leave.
/// </summary>
/// <remarks>
/// <para>
/// The cap is <see cref="Capacity"/> against OpenSSH's <c>MaxSessions</c>, 10 by default, which counts the session
/// channels of one connection: the shell, every exec, and an sftp subsystem on that connection. Forwarding channels
/// (tunnels, agent, X11) are not sessions and do not count, and the file browser's SFTP runs on a companion connection
/// of its own. The shell holds one, which leaves nine; six of them is the budget, because a channel still counts on the
/// server until its CLOSE has been handled there, after this side already disposed it, and because a hardened server
/// sets the limit lower than the default. Going over the limit fails the channel open outright.
/// </para>
/// <para>
/// A call that may run for minutes (a package transaction of up to 30, an image pull, a copy, the pieces of a file read
/// as root or from a container, a followed log) says so with <c>runsLong</c>, and long calls never hold more than <see cref="Capacity"/> minus
/// <see cref="ReservedForShort"/> slots together. Short calls finish within their own time limits, so the reserved
/// slots keep coming free and a read waits seconds at worst while an install runs; the long calls cannot starve them.
/// Waiting is first come, first served among the calls that could take the slot that came free, so a long call queued
/// behind a full long share is passed by short ones but is never passed by a later long one.
/// </para>
/// <para>
/// A slot is held only while a channel is open. Nothing here asks the user anything with a channel open (sudo asks for
/// its password between two channels), so a prompt left unanswered never holds a slot.
/// </para>
/// </remarks>
internal sealed class ExecChannelBudget
{
	/// <summary>Exec channels one connection may have open at once.</summary>
	public const int Capacity = 6;

	/// <summary>Slots a long call never takes, so short reads get through while installs and downloads run.</summary>
	public const int ReservedForShort = 2;

	/// <summary>A call allowed more than this counts as long: package changes, pulls and builds, not restarts.</summary>
	public static readonly TimeSpan LongAfter = TimeSpan.FromMinutes(5);

	private static readonly ConditionalWeakTable<SshClient, ExecChannelBudget> Budgets = new();

	private readonly Lock _lock = new();
	private readonly LinkedList<Waiter> _waiting = new();
	private readonly int _capacity;
	private readonly int _longCapacity;
	private int _held;
	private int _longHeld;

	internal ExecChannelBudget(int capacity = Capacity, int reservedForShort = ReservedForShort)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
		ArgumentOutOfRangeException.ThrowIfNegative(reservedForShort);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(reservedForShort, capacity);
		_capacity = capacity;
		_longCapacity = capacity - reservedForShort;
	}

	/// <summary>Slots taken right now.</summary>
	public int InUse
	{
		get
		{
			lock (_lock)
			{
				return _held;
			}
		}
	}

	/// <summary>Calls waiting for a slot.</summary>
	public int Waiting
	{
		get
		{
			lock (_lock)
			{
				return _waiting.Count;
			}
		}
	}

	/// <summary>The budget of the connection <paramref name="client"/> is, created on first use and gone with the client.</summary>
	public static ExecChannelBudget For(SshClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		return Budgets.GetValue(client, static _ => new ExecChannelBudget());
	}

	/// <summary>Whether a call allowed <paramref name="timeLimit"/> counts as long.</summary>
	public static bool RunsLong(TimeSpan timeLimit) => timeLimit > LongAfter;

	/// <summary>Waits for a slot. Dispose the slot once the channel it was for is closed.</summary>
	/// <exception cref="OperationCanceledException">The wait was cancelled; no slot was taken.</exception>
	public async ValueTask<ExecSlot> AcquireAsync(bool runsLong, CancellationToken cancellationToken)
	{
		Waiter waiter;
		lock (_lock)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Whenever the lock is free, nobody waiting could take a slot, so a call that can take one now passes nobody
			// it should not.
			if (CanTake(runsLong))
			{
				Take(runsLong);
				return new ExecSlot(this, runsLong);
			}

			waiter = new Waiter(this, runsLong);
			waiter.Node = _waiting.AddLast(waiter);
		}

		using (cancellationToken.UnsafeRegister(static (state, token) => ((Waiter)state!).Budget.Cancel((Waiter)state!, token), waiter))
		{
			return await waiter.Completion.Task;
		}
	}

	/// <summary>Gives a slot back and hands it, or another one this frees, to whoever waited longest and can take it.</summary>
	internal void Release(bool runsLong)
	{
		List<Waiter>? granted = null;
		lock (_lock)
		{
			_held--;
			if (runsLong)
			{
				_longHeld--;
			}

			LinkedListNode<Waiter>? node = _waiting.First;
			while (node is not null)
			{
				LinkedListNode<Waiter>? next = node.Next;
				Waiter waiter = node.Value;
				if (CanTake(waiter.RunsLong))
				{
					_waiting.Remove(node);
					waiter.Node = null;
					Take(waiter.RunsLong);
					(granted ??= []).Add(waiter);
				}

				node = next;
			}
		}

		foreach (Waiter waiter in granted ?? [])
		{
			waiter.Completion.SetResult(new ExecSlot(this, waiter.RunsLong));
		}
	}

	private bool CanTake(bool runsLong) => _held < _capacity && (!runsLong || _longHeld < _longCapacity);

	private void Take(bool runsLong)
	{
		_held++;
		if (runsLong)
		{
			_longHeld++;
		}
	}

	private void Cancel(Waiter waiter, CancellationToken cancellationToken)
	{
		lock (_lock)
		{
			// Already handed a slot: the caller gets it and gives it back.
			if (waiter.Node is null)
			{
				return;
			}

			_waiting.Remove(waiter.Node);
			waiter.Node = null;
		}

		waiter.Completion.SetCanceled(cancellationToken);
	}

	private sealed class Waiter(ExecChannelBudget budget, bool runsLong)
	{
		public ExecChannelBudget Budget { get; } = budget;

		public bool RunsLong { get; } = runsLong;

		/// <summary>Its place in the queue while it waits; null once it was handed a slot or gave up.</summary>
		public LinkedListNode<Waiter>? Node { get; set; }

		public TaskCompletionSource<ExecSlot> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	}
}
