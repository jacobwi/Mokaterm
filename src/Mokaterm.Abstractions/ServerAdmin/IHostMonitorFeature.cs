namespace Mokaterm.Abstractions.ServerAdmin;

/// <summary>
/// Live figures of the machine a session is logged in to: CPU, memory, the root file system, load and uptime. A session
/// offers it through <c>IProtocolSession.GetFeature</c> when it can run a short read on its machine, and releases it in
/// its teardown. The shell shows it without knowing which module it came from.
/// </summary>
/// <remarks>
/// Nothing is read until someone watches: <see cref="Watch"/> hands out a lease, the first lease starts the reads and
/// the last one to go stops them. However many watch, one read runs at a time, as often as the fastest lease asks, and
/// every watcher sees the same figures.
/// </remarks>
public interface IHostMonitorFeature
{
	/// <summary>Raised from any thread after a read went through or failed, and when the feature turns unavailable.</summary>
	event Action? Changed;

	/// <summary>What the feature knows now.</summary>
	HostMonitorState State { get; }

	/// <summary>
	/// The key of the session tool that shows these figures in full, which a click on them opens; null when the session
	/// has none.
	/// </summary>
	string? ToolKey { get; }

	/// <summary>
	/// Reads the machine every <paramref name="interval"/>, or more often while another lease asks for that, until the
	/// lease is disposed. The first read comes at once unless one went through less than an interval ago.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> is not positive.</exception>
	IDisposable Watch(TimeSpan interval);
}
