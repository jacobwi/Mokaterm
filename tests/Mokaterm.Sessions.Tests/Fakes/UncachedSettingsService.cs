using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Sessions.Tests.Fakes;

/// <summary>
/// Settings that keep only what was written: unlike the shared <see cref="FakeSettingsService"/>, a section nobody set
/// is a fresh default on every read and <see cref="ResetAsync{T}"/> removes it again, which is what lets these tests
/// watch a service re-read its section after a change.
/// </summary>
internal sealed class UncachedSettingsService : ISettingsService
{
	private readonly Lock _lock = new();
	private readonly Dictionary<Type, object> _sections = [];

	public event Action<string>? Changed;

	public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public T Get<T>() where T : class, ISettingsSection, new()
	{
		lock (_lock)
		{
			return _sections.TryGetValue(typeof(T), out object? section) ? (T)section : new T();
		}
	}

	public void Set<T>(T section) where T : class, ISettingsSection, new()
	{
		lock (_lock)
		{
			_sections[typeof(T)] = section;
		}

		Changed?.Invoke(T.SectionKey);
	}

	public Task UpdateAsync<T>(Func<T, T> update, CancellationToken cancellationToken = default) where T : class, ISettingsSection, new()
	{
		Set(update(Get<T>()));
		return Task.CompletedTask;
	}

	public Task ResetAsync<T>(CancellationToken cancellationToken = default) where T : class, ISettingsSection, new()
	{
		lock (_lock)
		{
			_ = _sections.Remove(typeof(T));
		}

		Changed?.Invoke(T.SectionKey);
		return Task.CompletedTask;
	}

	public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
