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
/// One command on several logins: a queue of hosts drained by at most <see cref="CommandRunSettings.ParallelHosts"/>
/// workers. Each worker connects without a tab, runs the command and closes its connection whatever happened, so no
/// connection outlives its host's result. Rows are immutable records swapped under the lock, so a reader on any thread
/// sees whole rows.
/// </summary>
internal sealed class CommandRun : ICommandRun
{
	/// <summary>
	/// How long disposing waits for the hosts it cancelled. A provider that ignores cancellation is left to finish on its
	/// own; its worker still closes the connection when the connect finally returns.
	/// </summary>
	internal static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

	private const string UnexpectedFailure = "The command could not run because of an unexpected error.";

	private readonly CommandRunner _owner;
	private readonly CommandRunSettings _options;
	private readonly CommandRunTarget[] _targets;
	private readonly Lock _lock = new();
	private readonly Queue<QueuedHost> _queue = new();
	private readonly List<Task> _workers = [];
	private readonly List<CancellationTokenSource> _batches = [];
	private CommandHostResult[] _hosts;
	private CancellationTokenSource _batch = new();
	private TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _active;
	private bool _disposed;

	public CommandRun(CommandRunner owner, string command, CommandRunSettings options, CommandRunTarget[] targets)
	{
		_owner = owner;
		Command = command;
		_options = options;
		_targets = targets;
		_hosts = [.. targets.Select(target => target.Describe(previous: null))];
		_batches.Add(_batch);
	}

	public event Action? Changed;

	public string Command { get; }

	public IReadOnlyList<CommandHostResult> Hosts
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
		RequeueAsync(static host => host.State is CommandHostState.Failed or CommandHostState.Cancelled, _options.SkipHostsThatAsk, cancellationToken);

	public Task<int> RetrySkippedAsync(CancellationToken cancellationToken = default) =>
		RequeueAsync(static host => host.State == CommandHostState.Skipped, skipPrompts: false, cancellationToken);

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
			_owner.Logger.LogWarning("A command run closed with hosts still connecting; each closes its connection once its connect gives up.");
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

	/// <summary>Queues every host. Called once, right after the runner created the run.</summary>
	internal void Start()
	{
		lock (_lock)
		{
			for (int index = 0; index < _targets.Length; index++)
			{
				Enqueue(index, _options.SkipHostsThatAsk);
			}
		}

		RaiseChanged();
		Pump();
	}

	private static string MessageOf(Exception exception) =>
		string.IsNullOrWhiteSpace(exception.Message) ? "The command could not run." : exception.Message;

	private async Task<int> RequeueAsync(Func<CommandHostResult, bool> which, bool skipPrompts, CancellationToken cancellationToken)
	{
		lock (_lock)
		{
			if (_disposed || !_hosts.Any(which))
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
				if (!which(_hosts[index]))
				{
					continue;
				}

				_targets[index] = CommandRunTarget.Resolve(catalog, _owner.Registry, _targets[index].ConnectionId);
				Enqueue(index, skipPrompts);
				queued++;
			}
		}

		if (queued > 0)
		{
			RaiseChanged();
			Pump();
		}

		return queued;
	}

	/// <summary>Puts a host in the queue, or fails it at once when it cannot run at all. Call under the lock.</summary>
	private void Enqueue(int index, bool skipPrompts)
	{
		CommandRunTarget target = _targets[index];
		CommandHostResult row = target.Describe(_hosts[index]) with
		{
			State = CommandHostState.Queued,
			Message = null,
			ExitCode = null,
			ExitSignal = null,
			Duration = null,
			Output = CommandOutputText.Empty,
			Error = CommandOutputText.Empty,
		};

		if (target.Problem is { } problem)
		{
			SetHost(index, row with { State = CommandHostState.Failed, Message = problem, Attempt = row.Attempt + 1 });
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
			while (_active < _options.ParallelHosts && _queue.TryDequeue(out QueuedHost next))
			{
				_active++;
				started = true;
				int index = next.Index;
				CommandRunTarget target = _targets[index];
				CancellationToken batch = _batch.Token;
				SetHost(index, _hosts[index] with { State = CommandHostState.Connecting, Message = null, Attempt = _hosts[index].Attempt + 1 });

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

	private async Task RunHostAsync(int index, CommandRunTarget target, bool skipPrompts, CancellationToken batch)
	{
		CommandHostResult result;
		try
		{
			result = await ConnectAndRunAsync(index, target, skipPrompts, batch);
		}
		catch (Exception ex)
		{
			// ConnectAndRunAsync maps every failure it knows of; this is one it did not expect.
			_owner.Logger.LogError("A host of a command run failed unexpectedly: {Failure}", LogSafe.Describe(ex));
			result = Current(index) with { State = CommandHostState.Failed, Message = UnexpectedFailure };
		}

		lock (_lock)
		{
			_active--;
			SetHost(index, result);
		}

		RaiseChanged();
		Pump();
	}

	private async Task<CommandHostResult> ConnectAndRunAsync(int index, CommandRunTarget target, bool skipPrompts, CancellationToken batch)
	{
		TimeProvider time = _owner.Time;
		long startedAt = time.GetTimestamp();
		Guid sessionId = Guid.NewGuid();
		using HostDeadline deadline = new(_options.Timeout, time, batch);
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
			IRemoteCommandFeature commands = session.GetFeature<IRemoteCommandFeature>()
				?? throw new RemoteCommandException("This connection cannot run commands.");

			running = true;
			Update(index, static host => host with { State = CommandHostState.Running, Message = null });
			RemoteCommandOutcome outcome = await commands.RunAsync(
				new RemoteCommandRequest { Command = Command, OutputLimitBytes = _options.OutputLimitBytes },
				deadline.Token);

			return Current(index) with
			{
				State = CommandHostState.Done,
				Message = null,
				ExitCode = outcome.ExitCode,
				ExitSignal = outcome.ExitSignal,
				Duration = outcome.Duration,
				Output = CommandOutputDecoder.Decode(outcome.Output),
				Error = CommandOutputDecoder.Decode(outcome.Error),
			};
		}
		catch (Exception) when (batch.IsCancellationRequested)
		{
			return Ended(index, CommandHostState.Cancelled, "Cancelled.", startedAt);
		}
		catch (Exception) when (deadline.TimedOut)
		{
			string seconds = _options.TimeoutSeconds.ToString(CultureInfo.CurrentCulture);
			return Ended(
				index,
				CommandHostState.Failed,
				running ? $"The command did not finish within {seconds} s." : $"Connecting took longer than {seconds} s.",
				startedAt);
		}
		catch (Exception) when (!running && prompts.SkippedBecause is { } reason)
		{
			// Whatever the provider made of the refused question, the host was skipped, not broken.
			return Ended(index, CommandHostState.Skipped, reason, startedAt);
		}
		catch (ProtocolConnectException ex) when (ex.Failure == ConnectFailure.Cancelled)
		{
			// The user closed one of this host's prompts.
			return Ended(index, CommandHostState.Cancelled, MessageOf(ex), startedAt);
		}
		catch (Exception ex) when (ex is ProtocolConnectException or RemoteCommandException or VaultLockedException)
		{
			return Ended(index, CommandHostState.Failed, MessageOf(ex), startedAt);
		}
		catch (OperationCanceledException)
		{
			// A cancel neither the run nor the clock asked for, such as a prompt that went away with its window.
			return Ended(index, CommandHostState.Cancelled, "Cancelled.", startedAt);
		}
		catch (Exception ex)
		{
			// Only the kind: the message of an unexpected failure is not known to be free of the command or its output.
			_owner.Logger.LogError("Session {SessionId} of a command run failed unexpectedly: {Failure}", sessionId, LogSafe.Describe(ex));
			return Ended(index, CommandHostState.Failed, UnexpectedFailure, startedAt);
		}
		finally
		{
			if (session is not null)
			{
				await SessionDisposal.DisposeAsync(session, sessionId, _owner.Logger, time);
			}
		}
	}

	private CommandHostResult Ended(int index, CommandHostState state, string message, long startedAt) => Current(index) with
	{
		State = state,
		Message = message,
		ExitCode = null,
		ExitSignal = null,
		Duration = _owner.Time.GetElapsedTime(startedAt),
		Output = CommandOutputText.Empty,
		Error = CommandOutputText.Empty,
	};

	private void ReportStatus(int index, string message)
	{
		if (Update(index, host => host.State == CommandHostState.Connecting ? host with { Message = message } : host))
		{
			RaiseChanged();
		}
	}

	private CommandHostResult Current(int index)
	{
		lock (_lock)
		{
			return _hosts[index];
		}
	}

	/// <summary>Replaces one row and says whether it changed. Raising <see cref="Changed"/> is the caller's.</summary>
	private bool Update(int index, Func<CommandHostResult, CommandHostResult> change)
	{
		lock (_lock)
		{
			CommandHostResult before = _hosts[index];
			CommandHostResult after = change(before);
			if (ReferenceEquals(before, after))
			{
				return false;
			}

			SetHost(index, after);
			return true;
		}
	}

	/// <summary>Swaps in a new array with one row replaced, so a list handed out earlier never changes under its reader. Call under the lock.</summary>
	private void SetHost(int index, CommandHostResult row)
	{
		CommandHostResult[] next = (CommandHostResult[])_hosts.Clone();
		next[index] = row;
		_hosts = next;
	}

	/// <summary>Marks every queued host cancelled and empties the queue. Call under the lock.</summary>
	private void CancelQueued()
	{
		while (_queue.TryDequeue(out QueuedHost queued))
		{
			SetHost(queued.Index, _hosts[queued.Index] with { State = CommandHostState.Cancelled, Message = "Cancelled before it started." });
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
