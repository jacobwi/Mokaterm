using System.Globalization;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Ssh.Connection;

namespace Mokaterm.Modules.Ssh.Admin.Reboot;

/// <summary>
/// The reconnects after a reboot that this window (or circuit) is waiting on, one per session. They belong to the
/// window rather than to the system view, because the view closes with the connection while the waiting goes on; the
/// view shows the one for its session whenever it is open. What happens is also written into the session's terminal
/// and said in a notice at the end, so the tab the user lands on tells the same story.
/// </summary>
internal sealed class RebootReconnects : IAsyncDisposable
{
	private readonly ISessionManager _sessions;
	private readonly IUserInteraction _interaction;
	private readonly TimeProvider _time;
	private readonly ILogger<RebootReconnects> _logger;
	private readonly Lock _lock = new();
	private readonly Dictionary<Guid, (RebootWatch Watch, CancellationTokenSource Stop)> _watches = [];
	private bool _disposed;

	public RebootReconnects(ISessionManager sessions, IUserInteraction interaction, TimeProvider time, ILogger<RebootReconnects> logger)
	{
		_sessions = sessions;
		_interaction = interaction;
		_time = time;
		_logger = logger;
	}

	/// <summary>Raised when a watch starts, changes or ends. Handlers may run on any thread.</summary>
	public event Action? Changed;

	/// <summary>The watch for <paramref name="sessionId"/>, running or the last one that ended, or null.</summary>
	public RebootWatch? Find(Guid sessionId)
	{
		lock (_lock)
		{
			return _watches.TryGetValue(sessionId, out (RebootWatch Watch, CancellationTokenSource Stop) entry) ? entry.Watch : null;
		}
	}

	/// <summary>
	/// Starts waiting for <paramref name="session"/> to come back after a reboot, replacing any watch it had. The server is
	/// asked whether it answers SSH before each reconnect when this machine reaches it directly; behind a jump host or a
	/// proxy each round simply reconnects.
	/// </summary>
	public RebootWatch Start(ISessionHandle session)
	{
		ArgumentNullException.ThrowIfNull(session);
		Func<CancellationToken, Task<bool>>? answers = session.Protocol.Id == SshProtocolIds.Ssh
			&& SshConnectionOptions.FromOptions(session.Connection.Options).ReachesTargetDirectly
				? token => PortGreeting.AnswersSshAsync(session.Host.Address, session.Connection.Port ?? session.Protocol.DefaultPort, token)
				: null;
		RebootWatch watch = new(
			session,
			token => _sessions.ReconnectAsync(session.Id, token),
			answers,
			() => _sessions.Find(session.Id) is not null,
			_time);
		CancellationTokenSource stop = new();
		lock (_lock)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_watches.TryGetValue(session.Id, out (RebootWatch Watch, CancellationTokenSource Stop) earlier))
			{
				earlier.Stop.Cancel();
			}

			_watches[session.Id] = (watch, stop);
		}

		watch.Changed += () => OnChanged(session, watch);
		_ = Task.Run(() => watch.RunAsync(stop.Token), CancellationToken.None);
		Changed?.Invoke();
		return watch;
	}

	/// <summary>Stops waiting for <paramref name="sessionId"/>; the session stays as it is.</summary>
	public void Stop(Guid sessionId)
	{
		lock (_lock)
		{
			if (_watches.TryGetValue(sessionId, out (RebootWatch Watch, CancellationTokenSource Stop) entry))
			{
				entry.Stop.Cancel();
			}
		}
	}

	public ValueTask DisposeAsync()
	{
		List<CancellationTokenSource> stops;
		lock (_lock)
		{
			if (_disposed)
			{
				return ValueTask.CompletedTask;
			}

			_disposed = true;
			stops = [.. _watches.Values.Select(entry => entry.Stop)];
			_watches.Clear();
		}

		// The sources stay undisposed: a watch still winding down may read its token once more.
		foreach (CancellationTokenSource stop in stops)
		{
			stop.Cancel();
		}

		return ValueTask.CompletedTask;
	}

	private void OnChanged(ISessionHandle session, RebootWatch watch)
	{
		lock (_lock)
		{
			if (!_watches.TryGetValue(session.Id, out (RebootWatch Watch, CancellationTokenSource Stop) entry) || !ReferenceEquals(entry.Watch, watch))
			{
				return;
			}
		}

		switch (watch.State)
		{
			case RebootWatchState.WaitingForServer when watch.Attempts == 0:
				Write(session, string.Create(CultureInfo.CurrentCulture, $"rebooting: waiting up to {watch.Times.Back.TotalMinutes:0} minutes for the server to answer SSH again"));
				break;
			case RebootWatchState.Back:
				_interaction.Notify(NoticeSeverity.Success, $"{session.Title} is back after its reboot.", "Reconnected");
				break;
			case RebootWatchState.GaveUp:
				_logger.LogInformation("Reconnecting session {SessionId} after a reboot gave up after {Attempts} attempts", session.Id, watch.Attempts);
				Write(session, watch.Problem ?? "stopped waiting for the server");
				_interaction.Notify(NoticeSeverity.Warning, watch.Problem ?? "The server did not come back.", "Not reconnected");
				break;
		}

		Changed?.Invoke();
	}

	private static void Write(ISessionHandle session, string notice)
	{
		if (session.Terminal is { } terminal)
		{
			_ = WriteAsync(terminal, notice);
		}
	}

	private static async Task WriteAsync(ITerminalStream terminal, string notice)
	{
		try
		{
			await terminal.WriteLocalAsync(TerminalNotice.Format(notice));
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
		{
			// The tab closed meanwhile.
		}
	}
}
