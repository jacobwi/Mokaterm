using System.Globalization;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Compares each read of the host's containers with the one before and says what changed on its own: a container that
/// turned unhealthy, stopped, or was restarted by the daemon. The first read only sets the baseline, a container seen
/// for the first time is never news, and a change the user ran is never reported, so each event is told once.
/// </summary>
/// <remarks>
/// A container in a restart loop would be news on every read, so after one restart notice the next waits
/// <see cref="RestartQuietFor"/> and then counts every restart since.
/// </remarks>
internal sealed class DockerHealthWatch
{
	public static readonly TimeSpan RestartQuietFor = TimeSpan.FromMinutes(5);

	private readonly Dictionary<string, (DateTimeOffset At, int Count)> _restartNotices = new(StringComparer.Ordinal);
	private Dictionary<string, DockerContainerState>? _last;
	private DateTimeOffset _lastRead;

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

			if (Alert(before, state, readEnded) is { } alert)
			{
				alerts.Add(alert);
			}
		}

		foreach (string gone in _restartNotices.Keys.Where(id => !current.ContainsKey(id)).ToList())
		{
			_restartNotices.Remove(gone);
		}

		return alerts;
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
