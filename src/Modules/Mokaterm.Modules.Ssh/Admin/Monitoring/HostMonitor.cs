using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Fleet;
using Mokaterm.Abstractions.ServerAdmin;

namespace Mokaterm.Modules.Ssh.Admin.Monitoring;

/// <summary>
/// A session's live figures: the overview's sample script, run as the login while anyone watches and as often as the
/// fastest watcher asks. The System view's overview and the shell's status bar read the same samples, so one session
/// never runs two of them at once. The session owns it and disposes it in its teardown.
/// </summary>
/// <remarks>
/// <para>
/// Reads never go through sudo and never ask anything: the sample only reads <c>/proc</c>, <c>/sys</c> and df, which
/// every login may. Each read is one short exec call under the sample's own time limit, so it takes its slot in the
/// connection's exec channel budget for no longer than that.
/// </para>
/// <para>
/// A login whose scripts never start (a forced command, an SFTP-only account) or a machine without <c>/proc</c> turns
/// the figures off for the session once; asking again every few seconds would only fail the same way. Any other failure
/// keeps the last figures and the next read tries again.
/// </para>
/// <para>
/// Public only so another session can offer the figures over its own commands (<see cref="ForCommands"/>), which is how
/// the workbench shows them without a server; what the overview uses stays internal.
/// </para>
/// </remarks>
public sealed class HostMonitor : IHostMonitorFeature, IAsyncDisposable
{
	internal const string CannotRunReason = "This login does not run scripts, so the machine's figures cannot be read.";

	internal const string NoProcReason = "The machine has no /proc to read its figures from, which only Linux has.";

	private const string ReadFailed = "The figures could not be read.";

	private readonly Func<AdminScript, CancellationToken, Task<AdminOutcome>> _read;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly Lock _lock = new();
	private readonly List<Lease> _leases = [];
	private TaskCompletionSource _wake = NewWake();
	private CancellationTokenSource? _running;
	private Task _loop = Task.CompletedTask;
	private HostMonitorState _state = HostMonitorState.Waiting;
	private long? _lastRead;
	private bool _disposed;

	/// <param name="read">Runs a script as the login, never as root: the session's system view feature.</param>
	/// <param name="toolKey">The session tool the figures open, or null when the session has none.</param>
	internal HostMonitor(Func<AdminScript, CancellationToken, Task<AdminOutcome>> read, TimeProvider time, ILogger logger, string? toolKey)
	{
		ArgumentNullException.ThrowIfNull(read);
		ArgumentNullException.ThrowIfNull(time);
		ArgumentNullException.ThrowIfNull(logger);
		_read = read;
		_time = time;
		_logger = logger;
		ToolKey = toolKey;
	}

	public event Action? Changed;

	public HostMonitorState State
	{
		get
		{
			lock (_lock)
			{
				return _state;
			}
		}
	}

	public string? ToolKey { get; }

	/// <summary>
	/// The overview's lines, which every sample that goes through extends. The reading loop writes it and a page reads it
	/// while it draws, so one figure may be a sample ahead of another for a frame; the next draw puts that right.
	/// </summary>
	internal MonitorHistory History { get; } = new();

	/// <summary>How many leases are held now.</summary>
	internal int Watchers
	{
		get
		{
			lock (_lock)
			{
				return _leases.Count;
			}
		}
	}

	/// <summary>The interval the reads follow: the fastest lease's, or null while nobody watches.</summary>
	internal TimeSpan? Interval
	{
		get
		{
			lock (_lock)
			{
				return _leases.Count == 0 ? null : _leases.Min(lease => lease.Interval);
			}
		}
	}

	/// <summary>
	/// Figures over any session's command feature, as the login. The SSH session builds its own over its system view
	/// feature; this is for a session that only has commands to offer.
	/// </summary>
	/// <param name="toolKey">The session tool the figures open, such as the System view's <c>system</c>; null for none.</param>
	public static HostMonitor ForCommands(IRemoteCommandFeature commands, TimeProvider time, string? toolKey, ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(commands);
		CommandFeatureAdminShell shell = new(commands, account: "");
		return new HostMonitor(
			(script, cancellationToken) => shell.RunAsync(script, elevated: false, input: null, cancellationToken),
			time,
			logger ?? NullLogger.Instance,
			toolKey);
	}

	public IDisposable Watch(TimeSpan interval)
	{
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
		Lease lease = new(this, interval);
		lock (_lock)
		{
			if (_disposed)
			{
				return lease;
			}

			_leases.Add(lease);
			if (_running is null && _state.Unavailable is null)
			{
				// A loop that was just stopped may still be finishing its read: the new one waits for it, so two reads
				// never run at once.
				CancellationTokenSource running = new();
				Task previous = _loop;
				_running = running;
				_loop = Task.Run(() => RunAsync(running, previous), CancellationToken.None);
			}
			else
			{
				_ = _wake.TrySetResult();
			}
		}

		return lease;
	}

	/// <summary>
	/// Takes a sample read elsewhere, such as the overview's first read, which prints the same lines: the figures start
	/// from it and the next read waits a whole interval.
	/// </summary>
	internal void Record(SystemSample sample)
	{
		ArgumentNullException.ThrowIfNull(sample);
		lock (_lock)
		{
			if (_disposed || _state.Unavailable is not null)
			{
				return;
			}

			_ = History.Add(sample);
			_lastRead = _time.GetTimestamp();
			_state = new HostMonitorState { Figures = FiguresOf(History, _time.GetUtcNow()) };
		}

		Raise();
	}

	public async ValueTask DisposeAsync()
	{
		CancellationTokenSource? running;
		Task loop;
		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_leases.Clear();
			running = _running;
			_running = null;
			loop = _loop;
		}

		Stop(running);
		await loop.ConfigureAwait(false);
	}

	/// <summary>What the newest sample and the rates before it say, in the shape the shell shows.</summary>
	internal static HostFigures FiguresOf(MonitorHistory history, DateTimeOffset readAt)
	{
		ArgumentNullException.ThrowIfNull(history);
		SystemSample sample = history.Latest ?? new SystemSample();
		MemoryInfo memory = sample.Memory;
		return new HostFigures
		{
			ReadAt = readAt,
			CpuPercent = history.Rates?.CpuPercent,
			Load1 = sample.Load?.One,
			Load5 = sample.Load?.Five,
			Load15 = sample.Load?.Fifteen,
			MemoryUsedBytes = Bytes(memory.UsedKb),
			MemoryTotalBytes = Bytes(memory.TotalKb),
			SwapUsedBytes = Bytes(memory.SwapUsedKb),
			SwapTotalBytes = Bytes(memory.SwapTotalKb),
			RootDisk = sample.RootDisk is { } disk
				? new HostDiskUsage
				{
					MountPoint = disk.MountPoint,
					TotalBytes = disk.TotalKb * 1024,
					UsedBytes = disk.UsedKb * 1024,
					UsedPercent = disk.UsedPercent,
				}
				: null,
			Uptime = sample.UptimeSeconds is { } seconds ? TimeSpan.FromSeconds(Math.Floor(seconds)) : null,
		};
	}

	private static long? Bytes(long? kb) => kb * 1024;

	private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	private static void Stop(CancellationTokenSource? running)
	{
		try
		{
			running?.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// That loop already ended on its own.
		}
	}

	private void Release(Lease lease)
	{
		CancellationTokenSource? running = null;
		lock (_lock)
		{
			if (!_leases.Remove(lease))
			{
				return;
			}

			if (_leases.Count > 0)
			{
				// The fastest lease may have gone, which makes the next read later.
				_ = _wake.TrySetResult();
				return;
			}

			running = _running;
			_running = null;
		}

		// Outside the lock: cancelling runs the read's own callbacks, which close its exec channel.
		Stop(running);
	}

	private async Task RunAsync(CancellationTokenSource running, Task previous)
	{
		CancellationToken stop = running.Token;
		try
		{
			await previous.ConfigureAwait(false);
			while (!stop.IsCancellationRequested)
			{
				Task woken;
				TimeSpan wait;
				lock (_lock)
				{
					// A loop that is no longer the current one stops here, whatever its token says yet.
					if (!ReferenceEquals(_running, running) || _leases.Count == 0)
					{
						return;
					}

					if (_wake.Task.IsCompleted)
					{
						_wake = NewWake();
					}

					woken = _wake.Task;
					wait = DueInLocked();
				}

				if (wait > TimeSpan.Zero)
				{
					// Woken early when a lease comes or goes, since the interval may have changed with it.
					using CancellationTokenSource timer = CancellationTokenSource.CreateLinkedTokenSource(stop);
					_ = await Task.WhenAny(Task.Delay(wait, _time, timer.Token), woken).ConfigureAwait(false);
					await timer.CancelAsync().ConfigureAwait(false);
					continue;
				}

				await ReadAsync(stop).ConfigureAwait(false);
			}
		}
		catch (Exception ex)
		{
			// Only a Changed handler can throw here. The loop ends, and the next lease starts a new one.
			_logger.LogWarning("The live figures of a session stopped: {Failure}", LogSafe.Describe(ex));
		}
		finally
		{
			lock (_lock)
			{
				if (ReferenceEquals(_running, running))
				{
					_running = null;
				}
			}

			running.Dispose();
		}
	}

	/// <summary>How long until the next read is due: at once when none was read yet or an interval has passed since.</summary>
	private TimeSpan DueInLocked()
	{
		if (_lastRead is not { } last)
		{
			return TimeSpan.Zero;
		}

		TimeSpan interval = _leases.Min(lease => lease.Interval);
		TimeSpan since = _time.GetElapsedTime(last);
		return since >= interval ? TimeSpan.Zero : interval - since;
	}

	private async Task ReadAsync(CancellationToken stop)
	{
		AdminOutcome outcome;
		try
		{
			outcome = await _read(MonitorCommands.Sample(), stop).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (stop.IsCancellationRequested)
		{
			return;
		}
		catch (AdminException ex) when (ex.NeverStarted)
		{
			TurnUnavailable(CannotRunReason, "its login does not run scripts");
			return;
		}
		catch (AdminException ex)
		{
			Fail(ex.Message);
			return;
		}
		catch (Exception ex)
		{
			_logger.LogInformation("Reading the live figures of a session failed: {Failure}", LogSafe.Describe(ex));
			Fail(ReadFailed);
			return;
		}

		SystemSample sample = MonitorCommands.Parse(outcome.Output);
		if (sample.ReadsNoProc)
		{
			TurnUnavailable(NoProcReason, "its machine has no /proc");
			return;
		}

		Record(sample);
	}

	private void Fail(string problem)
	{
		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			_lastRead = _time.GetTimestamp();
			_state = _state with { FailedReads = _state.FailedReads + 1, Problem = problem };
		}

		Raise();
	}

	/// <summary>Stops the reads for good. Called from the loop, which then ends by itself.</summary>
	private void TurnUnavailable(string reason, string why)
	{
		lock (_lock)
		{
			if (_disposed || _state.Unavailable is not null)
			{
				return;
			}

			_state = new HostMonitorState { Unavailable = reason };
			_running = null;
		}

		_logger.LogInformation("Live figures are off for a session because {Why}", why);
		Raise();
	}

	private void Raise() => Changed?.Invoke();

	/// <summary>One watcher. Disposing it twice lets go once.</summary>
	private sealed class Lease(HostMonitor owner, TimeSpan interval) : IDisposable
	{
		private int _disposed;

		public TimeSpan Interval { get; } = interval;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				owner.Release(this);
			}
		}
	}
}
