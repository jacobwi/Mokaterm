using System.Collections.Concurrent;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Tests.Shared;

/// <summary>In-memory settings with no file behind them, defaulting every section.</summary>
internal sealed class FakeSettingsService : ISettingsService
{
	private readonly ConcurrentDictionary<Type, object> _sections = new();

	public event Action<string>? Changed;

	public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public T Get<T>() where T : class, ISettingsSection, new() => (T)_sections.GetOrAdd(typeof(T), static _ => new T());

	public void Set<T>(T section) where T : class, ISettingsSection, new()
	{
		_sections[typeof(T)] = section;
		Changed?.Invoke(T.SectionKey);
	}

	public Task UpdateAsync<T>(Func<T, T> update, CancellationToken cancellationToken = default) where T : class, ISettingsSection, new()
	{
		Set(update(Get<T>()));
		return Task.CompletedTask;
	}

	public Task ResetAsync<T>(CancellationToken cancellationToken = default) where T : class, ISettingsSection, new()
	{
		Set(new T());
		return Task.CompletedTask;
	}

	public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
