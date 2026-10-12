using System.Globalization;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Sessions.Commands;

/// <summary>
/// Work on several logins: a queue of hosts drained by at most <see cref="CommandRunSettings.ParallelHosts"/> workers.
/// Each worker connects without a tab, with prompts and the host verifier gated by the run's prompt rule and the
/// host's own clock, does this kind's work on the session and closes the connection whatever happened, so no
/// connection outlives its host's result. A command run and a survey differ only in that work and in what their row
/// keeps; everything else is here. Rows are immutable records swapped under the lock, so a reader on any thread sees
/// whole rows.
/// </summary>
/// <typeparam name="TResult">The row each host reports in.</typeparam>
internal abstract class HostRun<TResult> : IHostRun<TResult>
	where TResult : HostRunResult
{
	/// <summary>
	/// How long disposing waits for the hosts it cancelled. A provider that ignores cancellation is left to finish on its
	/// own; its worker still closes the connection when the connect finally returns.
	/// </summary>
	internal static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

	/// <summary>The title of a row whose login is gone from the catalog.</summary>
	protected const string DeletedLogin = "Deleted login";

	private readonly HostRunner _owner;
	private readonly Lock _lock = new();
	private readonly Queue<QueuedHost> _queue = new();
	private readonly List<Task> _workers = [];
	private readonly List<CancellationTokenSource> _batches = [];
	private readonly List<HostRunTarget> _targets = [];
	private TResult[] _hosts = [];
	private CancellationTokenSource _batch = new();
	private TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _active;
	private bool _disposed;

	protected HostRun(HostRunner owner, CommandRunSettings options)
	{
		_owner = owner;
		Options = options;
		_batches.Add(_batch);
	}

	public event Action? Changed;

	public IReadOnlyList<TResult> Hosts
	{
		get
		{
			lock (_lock)
			{
				return _hosts;
			}
		}
	}

	public bool IsRunning
	{
		get
		{
			lock (_lock)
			{
				return _active > 0 || _queue.Count > 0;
			}
		}
	}

	protected CommandRunSettings Options { get; }

	protected TimeProvider Time => _owner.Time;

	/// <summary>What the run is called in the log, such as "command run".</summary>
	protected abstract string RunName { get; }

	/// <summary>The reason for a failure whose exception said nothing.</summary>
	protected abstract string FailedWithoutReason { get; }

	/// <summary>The reason for a failure nothing here expected. It never quotes the exception, which may carry what the host said.</summary>
	protected abstract string UnexpectedFailure { get; }

	public void CancelAll()
	{
		CancellationTokenSource cancelled;
		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			CancelQueued();

			// Hosts started after this get a fresh batch, so a retry is not born cancelled.
			cancelled = _batch;
			_batch = new CancellationTokenSource();
			_batches.Add(_batch);
			SignalIdleIfDone();
		}

		// Cancelling runs the workers' callbacks right here, so it happens outside the lock.
		try
		{
			cancelled.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// The run was disposed in between, which cancelled the same batch.
		}

		RaiseChanged();
	}

	public Task<int> RetryFailedAsync(CancellationToken cancellationToken = default) =>
		RequeueAsync(static host => host.State is HostRunState.Failed or HostRunState.Cancelled, [], Options.SkipHostsThatAsk, cancellationToken);

	public Task<int> RetrySkippedAsync(CancellationToken cancellationToken = default) =>
		RequeueAsync(static host => host.State == HostRunState.Skipped, [], skipPrompts: false, cancellationToken);

	public Task WaitAsync(CancellationToken cancellationToken = default)
	{
		Task idle;
		lock (_lock)
		{
			idle = _idle.Task;
		}

		return idle.WaitAsync(cancellationToken);
	}

	public async ValueTask DisposeAsync()
	{
		Task[] workers;
		CancellationTokenSource batch;
		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			CancelQueued();
			workers = [.. _workers];
			batch = _batch;
		}

		await batch.CancelAsync();
		bool drained = true;
		try
		{
			await Task.WhenAll(workers).WaitAsync(DrainTimeout, _owner.Time);
		}
		catch (TimeoutException)
		{
			drained = false;
			_owner.Logger.LogWarning("A {Run} closed with hosts still connecting; each closes its connection once its connect gives up.", RunName);
		}

		lock (_lock)
		{
			_ = _idle.TrySetResult();
		}

		_owner.Forget(this);

		// A worker that is still connecting holds a token of its batch, so its source stays until the worker is gone.
		if (drained)
		{
			foreach (CancellationTokenSource source in _batches)
			{
				source.Dispose();
			}
		}
	}

	/// <summary>Adds a row per login and queues every host. Called once, right after the runner created the run.</summary>
	internal void Start(ConnectionCatalog catalog, IReadOnlyList<Guid> connectionIds)
	{
		lock (_lock)
		{
			foreach (Guid connectionId in connectionIds)
			{
				Enqueue(Add(catalog, connectionId), Options.SkipHostsThatAsk);
			}
		}

		RaiseChanged();
		Pump();
	}

	/// <summary>Null when a login over <paramref name="protocol"/> can do this kind's work; otherwise why it cannot.</summary>
	protected abstract string? WhyNot(ProtocolDescriptor? protocol);

	/// <summary>A row for a login the catalog does not have; <see cref="HostRunTarget.Describe{T}"/> fills in what it does have.</summary>
	protected abstract TResult Blank(Guid connectionId);

	/// <summary>
	/// The row without what this kind's work left on it, for a new attempt and for an attempt that ended without
	/// finishing. A kind whose last result stays meaningful returns the row as it is.
	/// </summary>
	protected abstract TResult WithoutResult(TResult row);

	/// <summary>
	/// Does this kind's work on <paramref name="session"/> and returns the row with its result and duration;
	/// <see cref="HostRunState.Done"/> is set here. <paramref name="started"/> marks the host running once the session
	/// offers what the work needs, which is the line between a slow connect and slow work, and returns the row as it
	/// then stands.
	/// </summary>
	/// <exception cref="RemoteCommandException">The work could not run or did not run to its end, with a message for the user.</exception>
	protected abstract Task<TResult> RunOnSessionAsync(IProtocolSession session, Func<TResult> started, CancellationToken cancellationToken);

	/// <summary>The reason for a host whose work outlasted its time, such as "The command did not finish within 60 s."</summary>
	protected abstract string TimedOutWhileRunning(string seconds);

	/// <summary>
	/// Queues the hosts that <paramref name="which"/> picks and adds the logins in <paramref name="added"/> that the run
	/// does not have yet, all read afresh from the catalog. A host still pending is never picked twice. Returns how many
	/// were queued.
	/// </summary>
	protected async Task<int> RequeueAsync(Func<TResult, bool> which, IReadOnlyCollection<Guid> added, bool skipPrompts, CancellationToken cancellationToken)
	{
		lock (_lock)
		{
			if (_disposed || (added.Count == 0 && !_hosts.Any(host => !host.IsPending && which(host))))
			{
				return 0;
			}
		}

		ConnectionCatalog catalog = await _owner.LoadCatalogAsync(cancellationToken);
		int queued = 0;
		lock (_lock)
		{
			if (_disposed)
			{
				return 0;
			}

			for (int index = 0; index < _hosts.Length; index++)
			{
				// Read again under the lock: a second retry that ran while the catalog loaded already took these.
				if (_hosts[index].IsPending || !which(_hosts[index]))
				{
					continue;
				}

				_targets[index] = Resolve(catalog, _targets[index].ConnectionId);
				Enqueue(index, skipPrompts);
				queued++;
			}

			foreach (Guid connectionId in added.Distinct())
			{
				if (!_targets.Exists(target => target.ConnectionId == connectionId))
				{
					Enqueue(Add(catalog, connectionId), skipPrompts);
					queued++;
				}
			}
		}

		if (queued > 0)
		{
			RaiseChanged();
			Pump();
		}

		return queued;
	}

	private static TResult Restate(TResult row, HostRunState state, string? message)
	{
		HostRunResult changed = ((HostRunResult)row) with { State = state, Message = message };
		return (TResult)changed;
	}

	private HostRunTarget Resolve(ConnectionCatalog catalog, Guid connectionId) =>
		HostRunTarget.Resolve(catalog, _owner.Registry, connectionId, WhyNot);

	/// <summary>Adds a row at the end for a login the run did not have and returns its index. Call under the lock.</summary>
	private int Add(ConnectionCatalog catalog, Guid connectionId)
	{
		HostRunTarget target = Resolve(catalog, connectionId);
		_targets.Add(target);
		_hosts = [.. _hosts, target.Describe(Blank(connectionId))];
		return _hosts.Length - 1;
	}

	private string MessageOf(Exception exception) =>
		string.IsNullOrWhiteSpace(exception.Message) ? FailedWithoutReason : exception.Message;

	/// <summary>Puts a host in the queue, or fails it at once when it cannot run at all. Call under the lock.</summary>
	private void Enqueue(int index, bool skipPrompts)
	{
		HostRunTarget target = _targets[index];
		HostRunResult described = WithoutResult(target.Describe(_hosts[index]));
		TResult row = (TResult)(described with { State = HostRunState.Queued, Message = null, Duration = null });

		if (target.Problem is { } problem)
		{
			HostRunResult failed = ((HostRunResult)row) with { State = HostRunState.Failed, Message = problem, Attempt = row.Attempt + 1 };
			SetHost(index, (TResult)failed);
			return;
		}

		SetHost(index, row);
		_queue.Enqueue(new QueuedHost(index, skipPrompts));
		if (_idle.Task.IsCompleted)
		{
			_idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		}
	}

	/// <summary>Starts queued hosts while the parallel limit has room.</summary>
	private void Pump()
	{
		bool started = false;
		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			_ = _workers.RemoveAll(static worker => worker.IsCompleted);
			while (_active < Options.ParallelHosts && _queue.TryDequeue(out QueuedHost next))
			{
				_active++;
				started = true;
				int index = next.Index;
				HostRunTarget target = _targets[index];
				CancellationToken batch = _batch.Token;
				HostRunResult connecting = ((HostRunResult)_hosts[index]) with { State = HostRunState.Connecting, Message = null, Attempt = _hosts[index].Attempt + 1 };
				SetHost(index, (TResult)connecting);

				// The pool keeps the connect flow and its continuations off whatever context called in, such as a renderer.
				_workers.Add(Task.Run(() => RunHostAsync(index, target, next.SkipPrompts, batch)));
			}

			SignalIdleIfDone();
		}

		if (started)
		{
			RaiseChanged();
		}
	}

	private async Task RunHostAsync(int index, HostRunTarget target, bool skipPrompts, CancellationToken batch)
	{
		TResult result;
		try
		{
			result = await ConnectAndRunAsync(index, target, skipPrompts, batch);
		}
		catch (Exception ex)
		{
			// ConnectAndRunAsync maps every failure it knows of; this is one it did not expect.
			_owner.Logger.LogError("A host of a {Run} failed unexpectedly: {Failure}", RunName, LogSafe.Describe(ex));
			result = Restate(Current(index), HostRunState.Failed, UnexpectedFailure);
		}

		lock (_lock)
		{
			_active--;
			SetHost(index, result);
		}

		RaiseChanged();
		Pump();
	}

	private async Task<TResult> ConnectAndRunAsync(int index, HostRunTarget target, bool skipPrompts, CancellationToken batch)
	{
		TimeProvider time = _owner.Time;
		long startedAt = time.GetTimestamp();
		Guid sessionId = Guid.NewGuid();
		using HostDeadline deadline = new(Options.Timeout, time, batch);
		HostPromptGate prompts = new(_owner.Interaction, skipPrompts, deadline);
		IProtocolSession? session = null;
		bool running = false;
		try
		{
			// Resolve only queues a target with all three, so a null here is a bug, not a deleted login.
			DetachedConnect connect = new()
			{
				SessionId = sessionId,
				Host = target.Host ?? throw new InvalidOperationException("The target has no host."),
				Connection = target.Connection ?? throw new InvalidOperationException("The target has no login."),
				Protocol = target.Protocol ?? throw new InvalidOperationException("The target has no protocol."),
				Purpose = ConnectPurpose.Commands,
				Interaction = prompts,
				Verifier = new RunHostVerifier(_owner.Verifier, _owner.KnownHosts, _owner.Settings, prompts, deadline),
				Status = new InlineProgress<string>(message => ReportStatus(index, message)),
			};

			session = await _owner.Connector.ConnectAsync(connect, deadline.Token);
			TResult finished = await RunOnSessionAsync(session, Started, deadline.Token);
			return Restate(finished, HostRunState.Done, message: null);
		}
		catch (Exception) when (batch.IsCancellationRequested)
		{
			return Ended(index, HostRunState.Cancelled, "Cancelled.", startedAt);
		}
		catch (Exception) when (deadline.TimedOut)
		{
			string seconds = Options.TimeoutSeconds.ToString(CultureInfo.CurrentCulture);
			return Ended(index, HostRunState.Failed, running ? TimedOutWhileRunning(seconds) : $"Connecting took longer than {seconds} s.", startedAt);
		}
		catch (Exception) when (!running && prompts.SkippedBecause is { } reason)
		{
			// Whatever the provider made of the refused question, the host was skipped, not broken.
			return Ended(index, HostRunState.Skipped, reason, startedAt);
		}
		catch (ProtocolConnectException ex) when (ex.Failure == ConnectFailure.Cancelled)
		{
			// The user closed one of this host's prompts.
			return Ended(index, HostRunState.Cancelled, MessageOf(ex), startedAt);
		}
		catch (Exception ex) when (ex is ProtocolConnectException or RemoteCommandException or VaultLockedException)
		{
			return Ended(index, HostRunState.Failed, MessageOf(ex), startedAt);
		}
		catch (OperationCanceledException)
		{
			// A cancel neither the run nor the clock asked for, such as a prompt that went away with its window.
			return Ended(index, HostRunState.Cancelled, "Cancelled.", startedAt);
		}
		catch (Exception ex)
		{
			// Only the kind: the message of an unexpected failure is not known to be free of the command or its output.
			_owner.Logger.LogError("Session {SessionId} of a {Run} failed unexpectedly: {Failure}", sessionId, RunName, LogSafe.Describe(ex));
			return Ended(index, HostRunState.Failed, UnexpectedFailure, startedAt);
		}
		finally
		{
			if (session is not null)
			{
				await SessionDisposal.DisposeAsync(session, sessionId, _owner.Logger, time);
			}
		}

		TResult Started()
		{
			running = true;
			_ = Update(index, static host => Restate(host, HostRunState.Running, message: null));
			RaiseChanged();
			return Current(index);
		}
	}

	private TResult Ended(int index, HostRunState state, string message, long startedAt)
	{
		HostRunResult ended = ((HostRunResult)WithoutResult(Current(index))) with
		{
			State = state,
			Message = message,
			Duration = _owner.Time.GetElapsedTime(startedAt),
		};

		return (TResult)ended;
	}

	private void ReportStatus(int index, string message)
	{
		if (Update(index, host => host.State == HostRunState.Connecting ? Restate(host, HostRunState.Connecting, message) : host))
		{
			RaiseChanged();
		}
	}

	private TResult Current(int index)
	{
		lock (_lock)
		{
			return _hosts[index];
		}
	}

	/// <summary>Replaces one row and says whether it changed. Raising <see cref="Changed"/> is the caller's.</summary>
	private bool Update(int index, Func<TResult, TResult> change)
	{
		lock (_lock)
		{
			TResult before = _hosts[index];
			TResult after = change(before);
			if (ReferenceEquals(before, after))
			{
				return false;
			}

			SetHost(index, after);
			return true;
		}
	}

	/// <summary>Swaps in a new array with one row replaced, so a list handed out earlier never changes under its reader. Call under the lock.</summary>
	private void SetHost(int index, TResult row)
	{
		TResult[] next = (TResult[])_hosts.Clone();
		next[index] = row;
		_hosts = next;
	}

	/// <summary>Marks every queued host cancelled and empties the queue. Call under the lock.</summary>
	private void CancelQueued()
	{
		while (_queue.TryDequeue(out QueuedHost queued))
		{
			SetHost(queued.Index, Restate(_hosts[queued.Index], HostRunState.Cancelled, "Cancelled before it started."));
		}
	}

	/// <summary>Call under the lock.</summary>
	private void SignalIdleIfDone()
	{
		if (_active == 0 && _queue.Count == 0)
		{
			_ = _idle.TrySetResult();
		}
	}

	private void RaiseChanged() => EventRaiser.Raise(Changed, _owner.Logger, nameof(Changed));

	private readonly record struct QueuedHost(int Index, bool SkipPrompts);
}
