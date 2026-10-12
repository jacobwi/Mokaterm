namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The changes the user ran in this session and when, so the health watch can tell what the user did from what
/// happened on its own. Kept for a few minutes only: the watch looks back no further than its last two reads.
/// </summary>
internal sealed class DockerChangeLog
{
	private static readonly TimeSpan KeptFor = TimeSpan.FromMinutes(5);

	private readonly TimeProvider _time;
	private readonly Lock _lock = new();
	private readonly List<Entry> _entries = [];

	public DockerChangeLog(TimeProvider time)
	{
		ArgumentNullException.ThrowIfNull(time);
		_time = time;
	}

	/// <summary>Notes a change from now until the returned handle is disposed, which is when it ended.</summary>
	public IDisposable Begin(IReadOnlyList<DockerCall> calls)
	{
		ArgumentNullException.ThrowIfNull(calls);
		Entry entry = new(DockerChangeScope.Of(calls), _time.GetUtcNow());
		lock (_lock)
		{
			DateTimeOffset cutoff = entry.Started - KeptFor;
			_entries.RemoveAll(old => old.Ended is { } ended && ended < cutoff);
			_entries.Add(entry);
		}

		return new Ending(this, entry);
	}

	/// <summary>True when a change that covers <paramref name="container"/> ran at any moment from <paramref name="from"/> to <paramref name="to"/>.</summary>
	public bool Touched(DockerContainerState container, DateTimeOffset from, DateTimeOffset to)
	{
		ArgumentNullException.ThrowIfNull(container);
		lock (_lock)
		{
			return _entries.Any(entry => entry.Started <= to && (entry.Ended ?? DateTimeOffset.MaxValue) >= from && entry.Scope.Covers(container));
		}
	}

	private void End(Entry entry)
	{
		lock (_lock)
		{
			entry.Ended ??= _time.GetUtcNow();
		}
	}

	private sealed class Entry(DockerChangeScope scope, DateTimeOffset started)
	{
		public DockerChangeScope Scope { get; } = scope;

		public DateTimeOffset Started { get; } = started;

		public DateTimeOffset? Ended { get; set; }
	}

	private sealed class Ending(DockerChangeLog log, Entry entry) : IDisposable
	{
		public void Dispose() => log.End(entry);
	}
}
