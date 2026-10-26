namespace Mokaterm.Modules.Ssh.Docker;

// Fed by the health watch's reads and kept for the session, so a page opened later still knows which containers loop.
internal sealed class DockerRestartLoops
{
	private const int RunsKept = 5;

	private readonly Lock _lock = new();
	private readonly Dictionary<string, Track> _tracks = new(StringComparer.Ordinal);
	private DateTimeOffset? _lastRead;

	// Raised from the reading thread when a container enters or leaves a loop, or one in a loop moves on.
	public event Action? Changed;

	public IReadOnlyList<DockerRestartRecord> Looping
	{
		get
		{
			lock (_lock)
			{
				return [.. _tracks.Values.Where(track => track.Looping).OrderBy(track => track.Name, StringComparer.Ordinal).Select(track => track.Record())];
			}
		}
	}

	public DockerRestartRecord? Find(string id)
	{
		ArgumentNullException.ThrowIfNull(id);
		lock (_lock)
		{
			return _tracks.TryGetValue(id, out Track? track) ? track.Record() : null;
		}
	}

	public DockerRestartRecord? FindLooping(string id, string? name = null)
	{
		ArgumentNullException.ThrowIfNull(id);
		lock (_lock)
		{
			if (_tracks.TryGetValue(id, out Track? exact))
			{
				return exact.Looping ? exact.Record() : null;
			}

			foreach (Track track in _tracks.Values)
			{
				if (track.Looping && track.Record() is { } record && record.Matches(id, name))
				{
					return record;
				}
			}

			return null;
		}
	}

	public bool IsLooping(string id)
	{
		ArgumentNullException.ThrowIfNull(id);
		lock (_lock)
		{
			return _tracks.TryGetValue(id, out Track? track) && track.Looping;
		}
	}

	// Returns the containers that entered a loop with this read. A change of the user's own (DockerChangeLog) starts a
	// container's count over, so restarts the user caused never add up to a loop.
	public IReadOnlyList<DockerRestartRecord> Observe(
		IReadOnlyList<DockerContainerState> states,
		DateTimeOffset readStarted,
		DateTimeOffset readEnded,
		DockerChangeLog changes)
	{
		ArgumentNullException.ThrowIfNull(states);
		ArgumentNullException.ThrowIfNull(changes);
		List<DockerRestartRecord> entered = [];
		bool changed;
		lock (_lock)
		{
			List<(string Id, int Count, string State)> before = Visible();
			DateTimeOffset? since = _lastRead;
			_lastRead = readStarted;
			Dictionary<string, DockerContainerState> current = states.DistinctBy(state => state.Id).ToDictionary(state => state.Id, StringComparer.Ordinal);
			foreach (DockerContainerState state in current.Values)
			{
				if (!_tracks.TryGetValue(state.Id, out Track? track))
				{
					track = new Track(state.Id);
					_tracks.Add(state.Id, track);
				}
				else if ((since is { } from && changes.Touched(state, from, readEnded)) || state.RestartCount < track.RestartCount)
				{
					// docker start and docker restart set the count back to zero, from here or from anywhere else.
					track.StartOver();
				}

				if (track.See(state, readStarted))
				{
					entered.Add(track.Record());
				}
			}

			foreach (string gone in _tracks.Keys.Where(id => !current.ContainsKey(id)).ToList())
			{
				_tracks.Remove(gone);
			}

			changed = !before.SequenceEqual(Visible());
		}

		if (changed)
		{
			Changed?.Invoke();
		}

		return entered;
	}

	private List<(string Id, int Count, string State)> Visible() =>
		[.. _tracks.Values.Where(track => track.Looping).OrderBy(track => track.Id, StringComparer.Ordinal).Select(track => (track.Id, track.RestartCount, track.State))];

	private sealed class Track(string id)
	{
		private readonly List<DockerRestartSample> _samples = [];
		private readonly List<TimeSpan> _runs = [];
		private DateTimeOffset? _loopingSince;
		private DockerRestartLoopReason _reason;
		private string? _project;
		private string _policy = "no";
		private DateTimeOffset? _runStarted;
		private DateTimeOffset? _runSeenSince;
		private DateTimeOffset? _lastRunEnded;
		private DockerContainerExit? _lastExit;

		public string Id { get; } = id;

		public string Name { get; private set; } = "";

		public string State { get; private set; } = "";

		public int RestartCount { get; private set; } = -1;

		public bool Looping { get; private set; }

		public void StartOver()
		{
			Leave();
			_samples.Clear();
			_runStarted = null;
			_runSeenSince = null;
		}

		// True when the container entered a loop with this read.
		public bool See(DockerContainerState state, DateTimeOffset at)
		{
			int previousCount = RestartCount;
			DateTimeOffset? previousRun = State == "running" ? _runStarted : null;
			Name = state.Name;
			State = state.State;
			RestartCount = state.RestartCount;
			_project = state.ComposeProject;
			_policy = state.RestartPolicy;
			NoteRun(state, previousCount, previousRun, at);
			if (state.State is not ("running" or "paused") && state.FinishedAt is { } finished)
			{
				_lastExit = new DockerContainerExit(state.ExitCode, state.OomKilled, state.Error, finished);
			}

			_samples.Add(new DockerRestartSample(at, state.RestartCount));
			Trim(at);

			if (DockerRestartLoop.HasStopped(state))
			{
				Leave();
				return false;
			}

			if (Looping)
			{
				if (state.State == "running" && _runSeenSince is { } seen && at - seen >= DockerRestartLoop.StableAfter(_runs))
				{
					Leave();
				}

				return false;
			}

			DockerRestartLoopReason? reason = DockerRestartLoop.RoseTooOften(_samples) ? DockerRestartLoopReason.RoseTooOften
				: DockerRestartLoop.IsRestartingNow(state) ? DockerRestartLoopReason.Restarting
				: null;
			if (reason is not { } why)
			{
				return false;
			}

			Looping = true;
			_loopingSince = at;
			_reason = why;
			return true;
		}

		public DockerRestartRecord Record()
		{
			(int recent, TimeSpan over) = DockerRestartLoop.RecentRestarts(_samples);
			return new DockerRestartRecord
			{
				Id = Id,
				Name = Name,
				ComposeProject = _project,
				IsLooping = Looping,
				LoopingSince = _loopingSince,
				Reason = _reason,
				State = State,
				RestartCount = Math.Max(0, RestartCount),
				RecentRestarts = recent,
				RecentSpan = over,
				RestartPolicy = _policy,
				LastExit = _lastExit,
				RecentRuns = [.. Enumerable.Reverse(_runs)],
			};
		}

		// The samples start over from the newest one, so the restarts that made this loop cannot make the next one.
		private void Leave()
		{
			Looping = false;
			_loopingSince = null;
			_runs.Clear();
			if (_samples.Count > 1)
			{
				_samples.RemoveRange(0, _samples.Count - 1);
			}
		}

		// A run's length is only taken where docker pairs its start and end: while the container waits to restart, or
		// when exactly one restart came between two reads, so the start seen before belongs to the run that ended.
		private void NoteRun(DockerContainerState state, int previousCount, DateTimeOffset? previousRun, DateTimeOffset at)
		{
			if (state.FinishedAt is { } finished && finished != _lastRunEnded)
			{
				DateTimeOffset? started = state.State == "running"
					? (state.RestartCount == previousCount + 1 && state.StartedAt != previousRun ? previousRun : null)
					: state.StartedAt;
				if (started is { } start && start < finished)
				{
					_runs.Add(finished - start);
					_lastRunEnded = finished;
					if (_runs.Count > RunsKept)
					{
						_runs.RemoveAt(0);
					}
				}
			}

			if (state.State != "running")
			{
				_runSeenSince = null;
			}
			else if (_runSeenSince is null || state.StartedAt != _runStarted || state.RestartCount != previousCount)
			{
				_runSeenSince = at;
			}

			_runStarted = state.StartedAt;
		}

		// Everything inside the window, and the one read before it, so a read after a long pause still has something to
		// compare against.
		private void Trim(DateTimeOffset now)
		{
			int inside = _samples.FindIndex(sample => now - sample.At <= DockerRestartLoop.Window);
			if (inside > 1)
			{
				_samples.RemoveRange(0, inside - 1);
			}
		}
	}
}
