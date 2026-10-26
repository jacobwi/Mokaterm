using System.Globalization;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Compares each read of the host's containers with the one before and says what changed on its own: a container that
/// turned unhealthy, stopped, or was restarted by the daemon. The first read only sets the baseline, a container seen
/// for the first time is never news, and a change the user ran is never reported, so each event is told once.
/// </summary>
/// <remarks>
/// A container that keeps restarting would be news on every read, so after one restart notice the next waits
/// <see cref="RestartQuietFor"/> and then counts every restart since. Once <see cref="DockerRestartLoops"/> says it is
/// in a loop, that is told once instead, and its restarts are not told one by one.
/// </remarks>
internal sealed class DockerHealthWatch
{
	public static readonly TimeSpan RestartQuietFor = TimeSpan.FromMinutes(5);

	private readonly Dictionary<string, (DateTimeOffset At, int Count)> _restartNotices = new(StringComparer.Ordinal);
	private readonly Dictionary<string, DateTimeOffset> _loopNotices = new(StringComparer.Ordinal);
	private readonly DockerRestartLoops _loops;
	private Dictionary<string, DockerContainerState>? _last;
	private DateTimeOffset _lastRead;

	public DockerHealthWatch(DockerRestartLoops? loops = null) => _loops = loops ?? new DockerRestartLoops();

	/// <param name="readStarted">When the read that printed <paramref name="states"/> began.</param>
	/// <param name="readEnded">When it ended: a change of the user's running at any point since the read before counts.</param>
	public IReadOnlyList<DockerHealthAlert> Observe(
		IReadOnlyList<DockerContainerState> states,
		DateTimeOffset readStarted,
		DateTimeOffset readEnded,
		DockerChangeLog changes)
	{
		ArgumentNullException.ThrowIfNull(states);
		ArgumentNullException.ThrowIfNull(changes);
		IReadOnlyList<DockerRestartRecord> entered = _loops.Observe(states, readStarted, readEnded, changes);
		Dictionary<string, DockerContainerState> current = states.DistinctBy(state => state.Id).ToDictionary(state => state.Id, StringComparer.Ordinal);
		Dictionary<string, DockerContainerState>? last = _last;
		DateTimeOffset since = _lastRead;
		_last = current;
		_lastRead = readStarted;
		if (last is null)
		{
			return [];
		}

		List<DockerHealthAlert> alerts = [];
		foreach (DockerContainerState state in current.Values)
		{
			if (!last.TryGetValue(state.Id, out DockerContainerState? before))
			{
				continue;
			}

			if (changes.Touched(state, since, readEnded))
			{
				_restartNotices.Remove(state.Id);
				continue;
			}

			if (entered.FirstOrDefault(loop => loop.Id == state.Id) is { } loop)
			{
				if (LoopAlert(loop, readEnded) is { } told)
				{
					alerts.Add(told);
				}

				continue;
			}

			if (Alert(before, state, readEnded) is { } alert && !(alert.Event == DockerHealthEvent.Restarted && _loops.IsLooping(state.Id)))
			{
				alerts.Add(alert);
			}
		}

		foreach (string gone in _restartNotices.Keys.Where(id => !current.ContainsKey(id)).ToList())
		{
			_restartNotices.Remove(gone);
		}

		foreach (string gone in _loopNotices.Keys.Where(id => !current.ContainsKey(id)).ToList())
		{
			_loopNotices.Remove(gone);
		}

		return alerts;
	}

	private DockerHealthAlert? LoopAlert(DockerRestartRecord loop, DateTimeOffset at)
	{
		_restartNotices[loop.Id] = (at, loop.RestartCount);
		if (_loopNotices.TryGetValue(loop.Id, out DateTimeOffset told) && at - told < RestartQuietFor)
		{
			return null;
		}

		_loopNotices[loop.Id] = at;
		return new DockerHealthAlert(DockerHealthEvent.RestartLoop, loop.Name, DockerRestartWording.Notice(loop)) { IsError = true };
	}

	private DockerHealthAlert? Alert(DockerContainerState before, DockerContainerState now, DateTimeOffset at)
	{
		int restarts = now.RestartCount - before.RestartCount;
		if (before.IsAlive && now.State is "exited" or "dead")
		{
			_restartNotices.Remove(now.Id);
			string after = restarts > 0 ? string.Create(CultureInfo.CurrentCulture, $" after restarting {Times(restarts)}") : "";
			return new DockerHealthAlert(
				DockerHealthEvent.Exited,
				now.Name,
				string.Create(CultureInfo.CurrentCulture, $"{now.Name} stopped on its own{after}, with exit code {now.ExitCode}."))
			{
				IsError = now.ExitCode != 0,
			};
		}

		if (restarts > 0)
		{
			bool told = _restartNotices.TryGetValue(now.Id, out (DateTimeOffset At, int Count) notice);
			if (told && at - notice.At < RestartQuietFor)
			{
				return null;
			}

			int count = now.RestartCount - (told ? notice.Count : before.RestartCount);
			_restartNotices[now.Id] = (at, now.RestartCount);
			return new DockerHealthAlert(
				DockerHealthEvent.Restarted,
				now.Name,
				string.Create(CultureInfo.CurrentCulture, $"Docker restarted {now.Name} {Times(count)} after its process ended ({Times(now.RestartCount)} in all)."));
		}

		return before.Health != DockerHealth.Unhealthy && now.Health == DockerHealth.Unhealthy && now.State == "running"
			? new DockerHealthAlert(DockerHealthEvent.TurnedUnhealthy, now.Name, $"{now.Name} turned unhealthy: its health check fails.")
			: null;
	}

	private static string Times(int count) => count == 1 ? "once" : string.Create(CultureInfo.CurrentCulture, $"{count} times");
}
