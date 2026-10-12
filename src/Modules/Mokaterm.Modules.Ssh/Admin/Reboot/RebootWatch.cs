using System.Globalization;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.Modules.Ssh.Admin.Reboot;

/// <summary>
/// Reconnects a session after the machine behind it was rebooted: waits for the session to drop, then, every so
/// often, asks whether the server answers SSH again and reconnects through the session manager's own reconnect, the
/// one the tab's Reconnect button uses. It stops once the session is back, after a time limit, when the login needs
/// the user (a refused password, a changed host key), or when the tab is closed.
/// </summary>
internal sealed class RebootWatch
{
	private readonly ISessionHandle _session;
	private readonly Func<CancellationToken, Task> _reconnect;
	private readonly Func<CancellationToken, Task<bool>>? _answers;
	private readonly Func<bool> _isOpen;
	private readonly TimeProvider _time;
	private readonly RebootWatchTimes _times;
	private readonly Lock _lock = new();
	private RebootWatchState _state;
	private string? _problem;
	private int _attempts;

	/// <param name="reconnect">Starts a reconnect of the session, as the shell's Reconnect does.</param>
	/// <param name="answers">Whether the server answers SSH now; null when it cannot be asked from here (behind a jump host or a proxy), and every round simply tries to reconnect.</param>
	/// <param name="isOpen">False once the session's tab is gone.</param>
	public RebootWatch(
		ISessionHandle session,
		Func<CancellationToken, Task> reconnect,
		Func<CancellationToken, Task<bool>>? answers,
		Func<bool> isOpen,
		TimeProvider time,
		RebootWatchTimes? times = null)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(reconnect);
		ArgumentNullException.ThrowIfNull(isOpen);
		ArgumentNullException.ThrowIfNull(time);
		_session = session;
		_reconnect = reconnect;
		_answers = answers;
		_isOpen = isOpen;
		_time = time;
		_times = times ?? RebootWatchTimes.Default;
		StartedAt = time.GetUtcNow();
	}

	/// <summary>Raised whenever <see cref="State"/> or <see cref="Attempts"/> changes. Handlers may run on any thread.</summary>
	public event Action? Changed;

	public Guid SessionId => _session.Id;

	public DateTimeOffset StartedAt { get; }

	public RebootWatchTimes Times => _times;

	public RebootWatchState State
	{
		get
		{
			lock (_lock)
			{
				return _state;
			}
		}
	}

	/// <summary>Why it gave up, for <see cref="RebootWatchState.GaveUp"/>.</summary>
	public string? Problem
	{
		get
		{
			lock (_lock)
			{
				return _problem;
			}
		}
	}

	/// <summary>How many reconnects were started.</summary>
	public int Attempts => Volatile.Read(ref _attempts);

	public bool IsRunning => State is RebootWatchState.WaitingForDown or RebootWatchState.WaitingForServer or RebootWatchState.Reconnecting;

	/// <summary>Runs until the session is back, or it gives up. Cancelling stops it as <see cref="RebootWatchState.Stopped"/>.</summary>
	public async Task RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			await RunCoreAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			Set(RebootWatchState.Stopped, null);
		}
	}

	private async Task RunCoreAsync(CancellationToken cancellationToken)
	{
		Set(RebootWatchState.WaitingForDown, null);
		if (!await WaitUntilAsync(() => _session.State != SessionState.Connected, _times.Down, cancellationToken))
		{
			Set(RebootWatchState.GaveUp, string.Create(CultureInfo.CurrentCulture,
				$"The machine was still up {Minutes(_times.Down)} after the reboot was sent, so nothing was reconnected."));
			return;
		}

		Set(RebootWatchState.WaitingForServer, null);
		DateTimeOffset deadline = _time.GetUtcNow() + _times.Back;

		// sshd is among the last things to stop and the first to start, so asking at once would only find it going.
		await Task.Delay(_times.FirstWait, _time, cancellationToken);
		while (_time.GetUtcNow() < deadline)
		{
			if (!_isOpen())
			{
				Set(RebootWatchState.Stopped, null);
				return;
			}

			switch (_session.State)
			{
				case SessionState.Connected:
					Set(RebootWatchState.Back, null);
					return;
				case SessionState.Connecting:
					// The session manager's own automatic reconnect, or the user's; this waits for it to settle.
					await WaitUntilAsync(() => _session.State != SessionState.Connecting, Remaining(deadline), cancellationToken);
					continue;
			}

			if (Attempts > 0 && _session.Failure is ConnectFailure.AuthenticationFailed or ConnectFailure.HostIdentityRejected or ConnectFailure.Cancelled)
			{
				Set(RebootWatchState.GaveUp, "The server is back, but the login needs you: " + (_session.StatusMessage ?? "it was refused."));
				return;
			}

			if (_answers is null || await _answers(cancellationToken))
			{
				Interlocked.Increment(ref _attempts);
				Set(RebootWatchState.Reconnecting, null);
				try
				{
					await _reconnect(cancellationToken);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					Set(RebootWatchState.GaveUp, "The session could not be reconnected: " + ex.Message);
					return;
				}

				// The attempt starts on the session manager's own thread, so it may not show as connecting at once.
				await WaitUntilAsync(() => _session.State is SessionState.Connecting or SessionState.Connected, _times.AttemptStart, cancellationToken);
				await WaitUntilAsync(() => _session.State != SessionState.Connecting, Remaining(deadline), cancellationToken);
				if (_session.State == SessionState.Connected)
				{
					Set(RebootWatchState.Back, null);
					return;
				}

				Set(RebootWatchState.WaitingForServer, null);
			}

			TimeSpan pause = Remaining(deadline);
			await Task.Delay(pause < _times.Retry ? pause : _times.Retry, _time, cancellationToken);
		}

		Set(RebootWatchState.GaveUp, string.Create(CultureInfo.CurrentCulture,
			$"The server did not answer SSH within {Minutes(_times.Back)} of going down. Reconnect from the session's tab once it is up."));
	}

	/// <summary>True as soon as <paramref name="condition"/> holds, false when <paramref name="timeout"/> ran out first.</summary>
	private async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken)
	{
		DateTimeOffset until = _time.GetUtcNow() + timeout;
		while (!condition())
		{
			TimeSpan left = until - _time.GetUtcNow();
			if (left <= TimeSpan.Zero)
			{
				return false;
			}

			TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
			void OnChanged() => changed.TrySetResult();
			_session.Changed += OnChanged;
			try
			{
				if (condition())
				{
					return true;
				}

				using CancellationTokenSource delay = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				Task timer = Task.Delay(left, _time, delay.Token);
				await Task.WhenAny(changed.Task, timer);
				await delay.CancelAsync();
				cancellationToken.ThrowIfCancellationRequested();
			}
			finally
			{
				_session.Changed -= OnChanged;
			}
		}

		return true;
	}

	private TimeSpan Remaining(DateTimeOffset deadline)
	{
		TimeSpan left = deadline - _time.GetUtcNow();
		return left > TimeSpan.Zero ? left : TimeSpan.Zero;
	}

	private void Set(RebootWatchState state, string? problem)
	{
		lock (_lock)
		{
			_state = state;
			_problem = problem;
		}

		Changed?.Invoke();
	}

	private static string Minutes(TimeSpan span) => span.TotalMinutes >= 1
		? string.Create(CultureInfo.CurrentCulture, $"{span.TotalMinutes:0} minutes")
		: string.Create(CultureInfo.CurrentCulture, $"{span.TotalSeconds:0} seconds");
}
