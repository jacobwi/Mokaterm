using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Core.Serialization;

namespace Mokaterm.Core.Settings;

/// <summary>
/// App settings in the plain <c>settings</c> document. All sections are kept as JSON so sections from modules that are
/// not loaded survive a save; typed sections are cached, and writes are debounced.
/// </summary>
internal sealed class SettingsService : ISettingsService, IDisposable
{
	public const string DocumentName = "settings";

	public static readonly TimeSpan WriteDelay = TimeSpan.FromMilliseconds(500);

	private readonly IAppDataStore _store;
	private readonly ILogger<SettingsService> _logger;
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _writeGate = new(1, 1);
	private readonly Dictionary<string, object> _sections = new(StringComparer.Ordinal);
	private readonly ITimer _writeTimer;
	private JsonObject _root = [];
	private Task? _loadTask;
	private bool _dirty;
	private bool _disposed;

	public SettingsService(IAppDataStore store, TimeProvider timeProvider, ILogger<SettingsService> logger)
	{
		_store = store;
		_logger = logger;
		_writeTimer = timeProvider.CreateTimer(_ => _ = WriteInBackgroundAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
	}

	public event Action<string>? Changed;

	public Task LoadAsync(CancellationToken cancellationToken = default)
	{
		Task loadTask;
		lock (_sync)
		{
			if (_loadTask is null || _loadTask.IsFaulted || _loadTask.IsCanceled)
			{
				// Not tied to one caller's token: every caller shares this load.
				_loadTask = Task.Run(LoadCoreAsync, CancellationToken.None);
			}

			loadTask = _loadTask;
		}

		return loadTask.WaitAsync(cancellationToken);
	}

	public T Get<T>() where T : class, ISettingsSection, new()
	{
		lock (_sync)
		{
			return GetLocked<T>();
		}
	}

	public async Task UpdateAsync<T>(Func<T, T> update, CancellationToken cancellationToken = default) where T : class, ISettingsSection, new()
	{
		ArgumentNullException.ThrowIfNull(update);
		await LoadAsync(cancellationToken);

		lock (_sync)
		{
			T current = GetLocked<T>();
			T updated = SettingsSanitizer.Sanitize(update(current) ?? throw new InvalidOperationException("A settings update returned null."));
			if (EqualityComparer<T>.Default.Equals(current, updated))
			{
				return;
			}

			_sections[T.SectionKey] = updated;
			_root[T.SectionKey] = JsonSerializer.SerializeToNode(updated, MokatermJson.Document);
			ScheduleWriteLocked();
		}

		Changed?.Invoke(T.SectionKey);
	}

	public async Task ResetAsync<T>(CancellationToken cancellationToken = default) where T : class, ISettingsSection, new()
	{
		await LoadAsync(cancellationToken);

		lock (_sync)
		{
			_sections[T.SectionKey] = SettingsSanitizer.Sanitize(new T());
			_root.Remove(T.SectionKey);
			ScheduleWriteLocked();
		}

		Changed?.Invoke(T.SectionKey);
	}

	public async Task FlushAsync(CancellationToken cancellationToken = default)
	{
		await _writeGate.WaitAsync(cancellationToken);
		try
		{
			JsonObject snapshot;
			lock (_sync)
			{
				if (!_dirty)
				{
					return;
				}

				// Taken inside the write gate, so an older snapshot can never be written after a newer one.
				snapshot = (JsonObject)_root.DeepClone();
				_dirty = false;
				if (!_disposed)
				{
					_writeTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
				}
			}

			try
			{
				await _store.WriteAsync(DocumentName, snapshot, cancellationToken);
			}
			catch
			{
				lock (_sync)
				{
					_dirty = true;
				}

				throw;
			}
		}
		finally
		{
			_writeGate.Release();
		}
	}

	public void Dispose()
	{
		lock (_sync)
		{
			_disposed = true;
		}

		_writeTimer.Dispose();

		// _writeGate is not disposed: a background write still finishing would otherwise throw on Release.
	}

	private async Task LoadCoreAsync()
	{
		JsonObject? root = await _store.ReadAsync<JsonObject>(DocumentName);
		List<string> changedKeys;
		lock (_sync)
		{
			_root = root ?? [];

			// Sections read before the load returned defaults; readers of those and of every loaded section refresh.
			changedKeys = [.. _sections.Keys.Union(_root.Select(pair => pair.Key), StringComparer.Ordinal)];
			_sections.Clear();
		}

		foreach (string key in changedKeys)
		{
			Changed?.Invoke(key);
		}
	}

	/// <summary>Call while holding <see cref="_sync"/>.</summary>
	private T GetLocked<T>() where T : class, ISettingsSection, new()
	{
		if (_sections.TryGetValue(T.SectionKey, out object? cached) && cached is T section)
		{
			return section;
		}

		section = SettingsSanitizer.Sanitize(ReadSectionLocked<T>());
		_sections[T.SectionKey] = section;
		return section;
	}

	/// <summary>Call while holding <see cref="_sync"/>.</summary>
	private T ReadSectionLocked<T>() where T : class, ISettingsSection, new()
	{
		if (_root[T.SectionKey] is not JsonObject node)
		{
			return new T();
		}

		try
		{
			return node.Deserialize<T>(MokatermJson.Document) ?? new T();
		}
		catch (JsonException ex)
		{
			_logger.LogWarning(ex, "Settings section {Section} could not be read; using defaults", T.SectionKey);
			return new T();
		}
	}

	/// <summary>Call while holding <see cref="_sync"/>.</summary>
	private void ScheduleWriteLocked()
	{
		_dirty = true;
		if (!_disposed)
		{
			_writeTimer.Change(WriteDelay, Timeout.InfiniteTimeSpan);
		}
	}

	private async Task WriteInBackgroundAsync()
	{
		try
		{
			await FlushAsync();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Settings could not be saved");
		}
	}
}
