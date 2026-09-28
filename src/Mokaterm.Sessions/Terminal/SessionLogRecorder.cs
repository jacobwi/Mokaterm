using System.Globalization;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Sessions.Terminal;

/// <summary>
/// Keeps one <see cref="SessionLogWriter"/> per session it records, following the session manager so a file is closed
/// when its tab goes away. The setting is read when a session first appears, so turning it on never starts five
/// recordings at once and turning it off never cuts one short; the per-session switch decides after that.
/// <para>
/// Nothing needs to resolve this early: whatever creates it scans the sessions already open, and
/// <see cref="ITerminalStream.Attach"/> hands a new sink the replay buffer first, so a recording that starts a moment
/// after a session opens still has the output from before it.
/// </para>
/// </summary>
internal sealed class SessionLogRecorder : ISessionLogRecorder, IAsyncDisposable
{
	/// <summary>Rows the settings page is given. A folder nobody ever emptied must not turn into a page that never renders.</summary>
	internal const int MaxListedFiles = 200;

	private const string Extension = ".log";

	private readonly ISessionManager _sessions;
	private readonly ISettingsService _settings;
	private readonly IAppEnvironment _environment;
	private readonly TimeProvider _time;
	private readonly ILogger<SessionLogRecorder> _logger;
	private readonly Lock _lock = new();
	private readonly Dictionary<Guid, Recording> _recordings = [];

	/// <summary>Sessions the setting has already been applied to, so it is applied exactly once each.</summary>
	private readonly HashSet<Guid> _seen = [];

	private bool _disposed;

	public SessionLogRecorder(
		ISessionManager sessions,
		ISettingsService settings,
		IAppEnvironment environment,
		TimeProvider timeProvider,
		ILogger<SessionLogRecorder> logger)
	{
		_sessions = sessions;
		_settings = settings;
		_environment = environment;
		_time = timeProvider;
		_logger = logger;
		_sessions.SessionsChanged += OnSessionsChanged;
		OnSessionsChanged();
	}

	public event Action? Changed;

	public string Folder => _settings.Get<SessionLogSettings>().FolderOrDefault(_environment.DataDirectory);

	public SessionLogStatus StatusFor(Guid sessionId)
	{
		lock (_lock)
		{
			return _recordings.TryGetValue(sessionId, out Recording? recording) ? recording.Writer.Status : SessionLogStatus.Off;
		}
	}

	public void SetRecording(Guid sessionId, bool record)
	{
		ISessionHandle? handle = record ? _sessions.Find(sessionId) : null;
		Recording? stopping = null;
		bool changed;
		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			if (record)
			{
				changed = handle is not null && StartLocked(handle);
			}
			else
			{
				changed = _recordings.Remove(sessionId, out stopping);
			}
		}

		if (stopping is not null)
		{
			Stop(stopping);
		}

		if (changed)
		{
			Raise();
		}
	}

	public IReadOnlyList<SessionLogFile> ListFiles()
	{
		try
		{
			DirectoryInfo directory = new(Folder);
			return directory.Exists
				? [.. directory.EnumerateFiles('*' + Extension, SearchOption.TopDirectoryOnly)
					.Select(Describe)
					.OrderByDescending(file => file.WrittenAt)
					.Take(MaxListedFiles)]
				: [];
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
		{
			_logger.LogWarning("Listing the session logs failed: {Failure}", LogSafe.Describe(ex));
			return [];
		}
	}

	public Stream OpenRead(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		if (name != Path.GetFileName(name) || !name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
		{
			// Only a name the listing gave out: nothing that reads a file must be able to walk out of the folder.
			throw new ArgumentException("That is not a session log file name.", nameof(name));
		}

		return new FileStream(Path.Combine(Folder, name), new FileStreamOptions
		{
			Mode = FileMode.Open,
			Access = FileAccess.Read,

			// The session that is writing this one holds it open, and reading it while it grows is the point.
			Share = FileShare.ReadWrite,
			Options = FileOptions.Asynchronous,
		});
	}

	public async ValueTask DisposeAsync()
	{
		Recording[] recordings;
		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			recordings = [.. _recordings.Values];
			_recordings.Clear();
			_seen.Clear();
		}

		_sessions.SessionsChanged -= OnSessionsChanged;
		foreach (Recording recording in recordings)
		{
			Detach(recording);
			await recording.Writer.DisposeAsync();
		}
	}

	private static SessionLogFile Describe(FileInfo file) => new()
	{
		Name = file.Name,
		Length = file.Length,
		WrittenAt = file.LastWriteTimeUtc,
	};

	/// <summary>
	/// The session as <c>user@host:port</c>, which names the file and heads it. A protocol without a port (a serial
	/// line, whose address is the port name) is written without one.
	/// </summary>
	private static string Endpoint(ISessionHandle handle)
	{
		string user = handle.Connection.Username is { Length: > 0 } username ? username + "@" : "";
		if (!handle.Protocol.UsesPort)
		{
			return user + handle.Host.Address;
		}

		int port = handle.Connection.Port ?? handle.Protocol.DefaultPort;
		return string.Create(CultureInfo.InvariantCulture, $"{user}{handle.Host.Address}:{port}");
	}

	private void OnSessionsChanged()
	{
		IReadOnlyList<ISessionHandle> open = _sessions.Sessions;
		List<Recording> stopping = [];
		bool changed = false;
		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			HashSet<Guid> ids = [.. open.Select(session => session.Id)];
			foreach (Guid closed in _recordings.Keys.Where(id => !ids.Contains(id)).ToList())
			{
				if (_recordings.Remove(closed, out Recording? recording))
				{
					stopping.Add(recording);
					changed = true;
				}
			}

			// A closed tab is forgotten, so reopening the same login is a new session and reads the setting again.
			_ = _seen.RemoveWhere(id => !ids.Contains(id));
			bool record = _settings.Get<SessionLogSettings>().RecordEverySession;
			foreach (ISessionHandle handle in open)
			{
				if (_seen.Add(handle.Id) && record)
				{
					changed |= StartLocked(handle);
				}
			}
		}

		foreach (Recording recording in stopping)
		{
			Stop(recording);
		}

		if (changed)
		{
			Raise();
		}
	}

	/// <summary>
	/// Attaches a writer to one session. Call while holding the lock: attaching is not IO (the file is created when the
	/// first output arrives) and the pump delivers on its own thread, so nothing calls back in here.
	/// </summary>
	private bool StartLocked(ISessionHandle handle)
	{
		if (handle.Terminal is not { } stream || _recordings.ContainsKey(handle.Id))
		{
			return false;
		}

		SessionLogSettings settings = _settings.Get<SessionLogSettings>();
		SessionLogWriter writer = new(
			settings.FolderOrDefault(_environment.DataDirectory),
			Endpoint(handle),
			TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone),
			settings,
			_logger);
		writer.Changed += Raise;
		_recordings[handle.Id] = new Recording(writer, stream.Attach(writer));
		return true;
	}

	private void Stop(Recording recording)
	{
		Detach(recording);
		_ = CloseAsync(recording.Writer);
	}

	private void Detach(Recording recording)
	{
		recording.Writer.Changed -= Raise;

		// Detaching first, so the writer's own close cannot race a chunk that is still on its way in.
		recording.Attachment.Dispose();
	}

	private async Task CloseAsync(SessionLogWriter writer)
	{
		try
		{
			await writer.DisposeAsync();
		}
		catch (Exception ex)
		{
			_logger.LogWarning("Closing a session log failed: {Failure}", LogSafe.Describe(ex));
		}
	}

	private void Raise() => EventRaiser.Raise(Changed, _logger, nameof(Changed));

	private sealed record Recording(SessionLogWriter Writer, IDisposable Attachment);
}
