using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The session's builds and pushes: one at a time, run in the background so a page change or a closed manager does not
/// stop them, the last one kept until it is dismissed or the next one starts. Disposed with the session, which stops a
/// run that is still going.
/// </summary>
internal sealed class DockerLiveRuns : IAsyncDisposable
{
	private readonly CancellationTokenSource _lifetime = new();
	private readonly TimeProvider _time;
	private readonly DockerChangeLog _changes;
	private readonly ILogger? _logger;
	private readonly Lock _lock = new();
	private DockerLiveRun? _current;
	private Task _running = Task.CompletedTask;
	private int _disposed;

	public DockerLiveRuns(TimeProvider time, DockerChangeLog changes, ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(time);
		ArgumentNullException.ThrowIfNull(changes);
		_time = time;
		_changes = changes;
		_logger = logger;
	}

	/// <summary>Raised when a run starts, ends or is dismissed. Handlers may run on any thread.</summary>
	public event Action? Changed;

	/// <summary>The run going now, or the last one until it is dismissed.</summary>
	public DockerLiveRun? Current
	{
		get
		{
			lock (_lock)
			{
				return _current;
			}
		}
	}

	public bool IsRunning => Current is { IsRunning: true };

	/// <summary>
	/// Starts <paramref name="plan"/> through <paramref name="client"/> and returns at once. Noted for the health watch for
	/// as long as it runs, so nothing it does is reported as having happened on its own.
	/// </summary>
	/// <param name="shown">Every call as the question showed it.</param>
	/// <param name="succeeded">Runs once every call exited 0, on whatever thread the run ended on.</param>
	/// <exception cref="InvalidOperationException">Another run is still going.</exception>
	public DockerLiveRun Start(DockerClient client, DockerLivePlan plan, IReadOnlyList<string> shown, Action? succeeded = null)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(plan);
		DockerLiveRun run;
		lock (_lock)
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
			if (_current is { IsRunning: true })
			{
				throw new InvalidOperationException("A build or a push is still running.");
			}

			run = new DockerLiveRun(plan, shown, _time, _lifetime.Token);
			_current = run;
			_running = RunAsync(client, run, succeeded);
		}

		Changed?.Invoke();
		return run;
	}

	/// <summary>Forgets a run that ended, so its page stops showing it.</summary>
	public void Dismiss(DockerLiveRun run)
	{
		ArgumentNullException.ThrowIfNull(run);
		lock (_lock)
		{
			if (!ReferenceEquals(_current, run) || run.IsRunning)
			{
				return;
			}

			_current = null;
		}

		Changed?.Invoke();
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await _lifetime.CancelAsync();
		Task running;
		lock (_lock)
		{
			running = _running;
		}

		await running;
		_lifetime.Dispose();
	}

	private async Task RunAsync(DockerClient client, DockerLiveRun run, Action? succeeded)
	{
		// The run starts once Start returned, so the page sees it as current before its first line.
		await Task.Yield();
		using IDisposable noted = _changes.Begin(run.Plan.Calls);
		try
		{
			await run.RunAsync(client);
			if (run.State == DockerLiveState.Succeeded)
			{
				succeeded?.Invoke();
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The run reports its own failures; anything else is a bug, and must not take the session down with it.
			_logger?.LogWarning("A Docker build or push failed unexpectedly: {Reason}", LogSafe.Describe(ex));
		}
		finally
		{
			Changed?.Invoke();
		}
	}
}
