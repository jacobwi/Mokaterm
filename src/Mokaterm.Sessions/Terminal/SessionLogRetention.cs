using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Sessions.Terminal;

/// <summary>
/// Deletes session logs: by the two rules in <see cref="SessionLogSettings"/>, which keep everything until the user sets
/// one, and on request from the settings page. One per process, because recorders are per window or circuit and all of
/// them write into the same folder, and only this sees every file a recording is still writing. Only names
/// <see cref="SessionLogNames.IsGenerated"/> recognises, directly in the folder, are ever touched.
/// </summary>
internal sealed class SessionLogRetention
{
	private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

	private readonly ISettingsService _settings;
	private readonly IAppEnvironment _environment;
	private readonly TimeProvider _time;
	private readonly ILogger<SessionLogRetention> _logger;
	private readonly Lock _lock = new();

	/// <summary>Full paths recordings hold open, with how many hold each, which is one unless a name was reused.</summary>
	private readonly Dictionary<string, int> _writing = new(PathComparer);

	// One cleanup at a time, and at most one more waiting: however many recordings close at once, the folder is read twice.
	// Held only on a pool thread, around file system calls with no await between them.
	private readonly Lock _cleanup = new();
	private int _queued;

	public SessionLogRetention(ISettingsService settings, IAppEnvironment environment, TimeProvider time, ILogger<SessionLogRetention> logger)
	{
		_settings = settings;
		_environment = environment;
		_time = time;
		_logger = logger;
	}

	public string Folder => _settings.Get<SessionLogSettings>().FolderOrDefault(_environment.DataDirectory);

	/// <summary>
	/// The files a rule would delete now. Age goes by when a file was last written; over the size cap the oldest go first
	/// until the rest fit. A file in <paramref name="inUse"/> still counts towards the total but is never picked.
	/// </summary>
	public static IReadOnlyList<SessionLogFile> Expired(
		IReadOnlyList<SessionLogFile> files,
		IReadOnlySet<string> inUse,
		SessionLogSettings settings,
		DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(files);
		ArgumentNullException.ThrowIfNull(inUse);
		ArgumentNullException.ThrowIfNull(settings);
		SessionLogSettings rules = settings.Clamped();
		List<SessionLogFile> doomed = [];
		HashSet<string> picked = new(PathComparer);
		if (rules.DeleteAfterDays > 0)
		{
			DateTimeOffset cutoff = now - TimeSpan.FromDays(rules.DeleteAfterDays);
			foreach (SessionLogFile file in files)
			{
				if (file.WrittenAt < cutoff && !inUse.Contains(file.Name) && picked.Add(file.Name))
				{
					doomed.Add(file);
				}
			}
		}

		if (rules.KeepAtMostMegabytes > 0)
		{
			long cap = rules.KeepAtMostMegabytes * 1024L * 1024L;
			long total = files.Where(file => !picked.Contains(file.Name)).Sum(file => Math.Max(0, file.Length));
			IEnumerable<SessionLogFile> oldestFirst = files
				.Where(file => !picked.Contains(file.Name))
				.OrderBy(file => file.WrittenAt)
				.ThenBy(file => file.Name, StringComparer.Ordinal);

			foreach (SessionLogFile file in oldestFirst)
			{
				if (total <= cap)
				{
					break;
				}

				if (inUse.Contains(file.Name) || !picked.Add(file.Name))
				{
					continue;
				}

				doomed.Add(file);
				total -= Math.Max(0, file.Length);
			}
		}

		return doomed;
	}

	/// <summary>Marks <paramref name="path"/> as being written until the returned handle is disposed.</summary>
	public IDisposable MarkWriting(string path)
	{
		string full = Path.GetFullPath(path);
		lock (_lock)
		{
			_writing[full] = _writing.GetValueOrDefault(full) + 1;
		}

		return new WritingMark(this, full);
	}

	/// <summary>
	/// Runs the rules in the background, unless neither is on. Calls while one runs are folded into one more run, which
	/// is what a window closing with five recordings open asks for.
	/// </summary>
	public void ScheduleCleanup()
	{
		if (!_settings.Get<SessionLogSettings>().DeletesOldLogs || Interlocked.Exchange(ref _queued, 1) == 1)
		{
			return;
		}

		_ = Task.Run(RunQueued);
	}

	/// <summary>Applies the rules now, off the caller's thread.</summary>
	public Task<SessionLogDeletion> CleanUpAsync(CancellationToken cancellationToken = default) =>
		Task.Run(
			() =>
			{
				lock (_cleanup)
				{
					return CleanUp();
				}
			},
			cancellationToken);

	/// <summary>Every generated log directly in the folder. Empty when there is no folder or it cannot be read.</summary>
	public IReadOnlyList<SessionLogFile> List() => List(Folder);

	/// <summary>
	/// Deletes the named files from the folder. A name that is not one of ours, or is not directly in the folder, is
	/// refused like a missing file; a file a recording is writing is left alone and counted.
	/// </summary>
	public SessionLogDeletion Delete(IEnumerable<string> names)
	{
		ArgumentNullException.ThrowIfNull(names);
		string folder = Folder;
		HashSet<string> wanted = new(names.Where(name => name == Path.GetFileName(name) && SessionLogNames.IsGenerated(name)), PathComparer);
		return Delete(folder, [.. List(folder).Where(file => wanted.Contains(file.Name))], "Deleting session logs");
	}

	/// <summary>Deletes every generated log in the folder but the ones recordings are writing.</summary>
	public SessionLogDeletion DeleteAll()
	{
		string folder = Folder;
		return Delete(folder, List(folder), "Deleting every session log");
	}

	private static SessionLogFile Describe(FileInfo file) => new()
	{
		Name = file.Name,
		Length = file.Length,
		WrittenAt = file.LastWriteTimeUtc,
	};

	private void RunQueued()
	{
		try
		{
			lock (_cleanup)
			{
				// Cleared once this run has the folder, so a recording that closes from here on queues another.
				_ = Interlocked.Exchange(ref _queued, 0);
				_ = CleanUp();
			}
		}
		catch (Exception ex)
		{
			// Nothing may fail a session or the start over a log folder; the next run tries again.
			_logger.LogWarning("Cleaning up session logs failed: {Failure}", LogSafe.Describe(ex));
		}
	}

	private SessionLogDeletion CleanUp()
	{
		SessionLogSettings settings = _settings.Get<SessionLogSettings>();
		if (!settings.DeletesOldLogs)
		{
			return SessionLogDeletion.None;
		}

		string folder = settings.FolderOrDefault(_environment.DataDirectory);
		IReadOnlyList<SessionLogFile> files = List(folder);
		IReadOnlyList<SessionLogFile> expired = Expired(files, InUse(folder, files), settings, _time.GetUtcNow());
		return expired.Count == 0 ? SessionLogDeletion.None : Delete(folder, expired, "Cleaning up session logs");
	}

	private IReadOnlyList<SessionLogFile> List(string folder)
	{
		try
		{
			DirectoryInfo directory = new(folder);
			return directory.Exists
				? [.. directory.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
					.Where(file => SessionLogNames.IsGenerated(file.Name))
					.Select(Describe)]
				: [];
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
		{
			_logger.LogWarning("Listing the session logs failed: {Failure}", LogSafe.Describe(ex));
			return [];
		}
	}

	private HashSet<string> InUse(string folder, IEnumerable<SessionLogFile> files)
	{
		HashSet<string> inUse = new(PathComparer);
		lock (_lock)
		{
			foreach (SessionLogFile file in files)
			{
				if (_writing.ContainsKey(FullPath(folder, file.Name)))
				{
					inUse.Add(file.Name);
				}
			}
		}

		return inUse;
	}

	private bool IsWriting(string fullPath)
	{
		lock (_lock)
		{
			return _writing.ContainsKey(fullPath);
		}
	}

	private SessionLogDeletion Delete(string folder, IReadOnlyList<SessionLogFile> files, string action)
	{
		int deleted = 0;
		int inUse = 0;
		int failed = 0;
		HashSet<string> failures = new(StringComparer.Ordinal);
		foreach (SessionLogFile file in files)
		{
			string path = FullPath(folder, file.Name);

			// Asked again right before the delete: a recording can have started since the folder was read.
			if (IsWriting(path))
			{
				inUse++;
				continue;
			}

			try
			{
				File.Delete(path);
				deleted++;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				// Another copy of the app may still be writing it, which Windows refuses as a sharing violation.
				failed++;
				failures.Add(LogSafe.Describe(ex));
			}
		}

		if (failed > 0)
		{
			// Types only: the message would name the folder, and the file names name the hosts.
			_logger.LogWarning("{Action}: {Failed} of {Count} files could not be deleted ({Failures}).", action, failed, files.Count, string.Join(", ", failures));
		}

		if (deleted > 0)
		{
			_logger.LogInformation("{Action}: {Deleted} files deleted.", action, deleted);
		}

		return new SessionLogDeletion { Deleted = deleted, InUse = inUse, Failed = failed };
	}

	private static string FullPath(string folder, string name) => Path.GetFullPath(Path.Combine(folder, name));

	private void Release(string fullPath)
	{
		lock (_lock)
		{
			int count = _writing.GetValueOrDefault(fullPath);
			if (count <= 1)
			{
				_ = _writing.Remove(fullPath);
			}
			else
			{
				_writing[fullPath] = count - 1;
			}
		}
	}

	private sealed class WritingMark(SessionLogRetention owner, string fullPath) : IDisposable
	{
		private int _released;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0)
			{
				owner.Release(fullPath);
			}
		}
	}
}
