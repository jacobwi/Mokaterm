using System.Collections.Immutable;

namespace Mokaterm.Abstractions.Protocols;

/// <summary>
/// Where one connect attempt stands: the steps its protocol planned, in order, and how each stands. Immutable, so a
/// reader on another thread always sees a whole chain; every change returns a new chain, or this one when nothing
/// changed. The methods are the rules <see cref="IConnectSteps"/> describes, kept here so the session manager and any
/// other sink of reports apply the same ones.
/// </summary>
public sealed class ConnectStepChain
{
	private readonly ImmutableArray<ConnectStepStatus> _steps;

	private ConnectStepChain(ImmutableArray<ConnectStepStatus> steps)
	{
		_steps = steps;
		Steps = steps;
	}

	/// <summary>No steps: the protocol reports none, or the attempt has not planned them yet.</summary>
	public static ConnectStepChain Empty { get; } = new([]);

	/// <summary>The steps in the order the attempt passes them.</summary>
	public IReadOnlyList<ConnectStepStatus> Steps { get; }

	/// <summary>True when there is nothing to draw, which leaves the session tab with its plain status line.</summary>
	public bool IsEmpty => _steps.IsEmpty;

	/// <summary>The step under way, if any.</summary>
	public ConnectStepStatus? Current => Find(ConnectStepState.Running);

	/// <summary>The step the attempt broke at, if any.</summary>
	public ConnectStepStatus? Failed => Find(ConnectStepState.Failed);

	/// <summary>A chain of <paramref name="steps"/>, all pending.</summary>
	/// <exception cref="ArgumentException">A step has no id, or two steps share one.</exception>
	public static ConnectStepChain Of(IEnumerable<ConnectStep> steps)
	{
		ArgumentNullException.ThrowIfNull(steps);
		ImmutableArray<ConnectStepStatus>.Builder planned = ImmutableArray.CreateBuilder<ConnectStepStatus>();
		HashSet<string> ids = new(StringComparer.Ordinal);
		foreach (ConnectStep step in steps)
		{
			if (step is null || string.IsNullOrWhiteSpace(step.Id) || !ids.Add(step.Id))
			{
				throw new ArgumentException(
					$"Every connect step needs an id of its own, and '{step?.Id}' is missing or used twice.",
					nameof(steps));
			}

			planned.Add(new ConnectStepStatus(step, ConnectStepState.Pending));
		}

		return planned.Count == 0 ? Empty : new ConnectStepChain(planned.ToImmutable());
	}

	/// <inheritdoc cref="IConnectSteps.Start"/>
	public ConnectStepChain Start(string stepId, string? detail = null) =>
		Change(stepId, Passed, status => Set(status, ConnectStepState.Running, detail), Reset);

	/// <inheritdoc cref="IConnectSteps.Complete"/>
	public ConnectStepChain Complete(string stepId) =>
		Change(stepId, Passed, Passed, Kept);

	/// <summary>
	/// The attempt broke at <paramref name="stepId"/>. A step before it that was still running held; one that was never
	/// reached stays pending, and every step after it that applied is pending again.
	/// </summary>
	public ConnectStepChain Fail(string stepId, string reason) =>
		Change(stepId, PassedIfRunning, status => Set(status, ConnectStepState.Failed, reason), ResetUnlessSkipped);

	/// <inheritdoc cref="IConnectSteps.Skip"/>
	public ConnectStepChain Skip(string stepId, string? reason = null) =>
		Change(stepId, Kept, status => Set(status, ConnectStepState.Skipped, reason), Kept);

	/// <summary>
	/// Marks where an attempt that ended in a failure stopped, unless a step already says it failed: the step under way,
	/// else the first one never reached, else the last one that applied, since a failure after every step held still
	/// belongs to the last of them.
	/// </summary>
	public ConnectStepChain FailCurrent(string reason)
	{
		if (Failed is not null)
		{
			return this;
		}

		int index = IndexWhere(status => status.State == ConnectStepState.Running, fromEnd: false);
		if (index < 0)
		{
			index = IndexWhere(status => status.State == ConnectStepState.Pending, fromEnd: false);
		}

		if (index < 0)
		{
			index = IndexWhere(status => status.State != ConnectStepState.Skipped, fromEnd: true);
		}

		return index < 0 ? this : Fail(_steps[index].Step.Id, reason);
	}

	/// <summary>The attempt connected, so every step that applied held, whatever was reported last.</summary>
	public ConnectStepChain CompleteAll() => Rebuild((_, status) => Passed(status));

	private static ConnectStepStatus Kept(ConnectStepStatus status) => status;

	private static ConnectStepStatus Passed(ConnectStepStatus status) => status.State switch
	{
		ConnectStepState.Pending or ConnectStepState.Running => Set(status, ConnectStepState.Done, status.Detail),
		ConnectStepState.Failed => Set(status, ConnectStepState.Done, null),
		_ => status,
	};

	private static ConnectStepStatus PassedIfRunning(ConnectStepStatus status) =>
		status.State == ConnectStepState.Running ? Set(status, ConnectStepState.Done, status.Detail) : status;

	private static ConnectStepStatus Reset(ConnectStepStatus status) => Set(status, ConnectStepState.Pending, null);

	// A failure ends the pass, so a step this pass already found does not apply stays that way. A start may begin a new
	// pass, where it might apply after all.
	private static ConnectStepStatus ResetUnlessSkipped(ConnectStepStatus status) =>
		status.State == ConnectStepState.Skipped ? status : Reset(status);

	private static ConnectStepStatus Set(ConnectStepStatus status, ConnectStepState state, string? detail) =>
		status.State == state && status.Detail == detail ? status : status with { State = state, Detail = detail };

	private ConnectStepChain Change(
		string stepId,
		Func<ConnectStepStatus, ConnectStepStatus> before,
		Func<ConnectStepStatus, ConnectStepStatus> at,
		Func<ConnectStepStatus, ConnectStepStatus> after)
	{
		int index = IndexWhere(status => string.Equals(status.Step.Id, stepId, StringComparison.Ordinal), fromEnd: false);
		return index < 0
			? this
			: Rebuild((i, status) => i < index ? before(status) : i == index ? at(status) : after(status));
	}

	// The chain is replaced only when a step came out different, so a report that changes nothing raises nothing.
	private ConnectStepChain Rebuild(Func<int, ConnectStepStatus, ConnectStepStatus> rule)
	{
		ImmutableArray<ConnectStepStatus>.Builder next = _steps.ToBuilder();
		bool changed = false;
		for (int i = 0; i < next.Count; i++)
		{
			ConnectStepStatus status = rule(i, next[i]);
			if (!ReferenceEquals(status, next[i]))
			{
				next[i] = status;
				changed = true;
			}
		}

		return changed ? new ConnectStepChain(next.ToImmutable()) : this;
	}

	private int IndexWhere(Func<ConnectStepStatus, bool> match, bool fromEnd)
	{
		for (int n = 0; n < _steps.Length; n++)
		{
			int i = fromEnd ? _steps.Length - 1 - n : n;
			if (match(_steps[i]))
			{
				return i;
			}
		}

		return -1;
	}

	private ConnectStepStatus? Find(ConnectStepState state)
	{
		int index = IndexWhere(status => status.State == state, fromEnd: false);
		return index < 0 ? null : _steps[index];
	}
}
