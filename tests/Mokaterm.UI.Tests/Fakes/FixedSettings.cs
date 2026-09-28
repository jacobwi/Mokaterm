using Mokaterm.Abstractions.Settings;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>
/// Settings that answer with defaults plus the sections the test seeded, and that keep what is written to them, so a
/// test can check what a page or an action saved.
/// </summary>
internal sealed class FixedSettings : ISettingsService
{
	// ISettingsSection has a static abstract member, so it cannot itself be a type argument.
	private readonly Dictionary<Type, object> _sections = [];

	public FixedSettings(params object[] sections)
	{
		foreach (object section in sections)
		{
			_sections[section.GetType()] = section;
		}
	}

	public event Action<string>? Changed;

	public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public T Get<T>() where T : class, ISettingsSection, new() =>
		_sections.GetValueOrDefault(typeof(T)) as T ?? new T();

	public Task UpdateAsync<T>(Func<T, T> update, CancellationToken cancellationToken = default)
		where T : class, ISettingsSection, new()
	{
		T updated = update(Get<T>());
		_sections[typeof(T)] = updated;
		Changed?.Invoke(T.SectionKey);
		return Task.CompletedTask;
	}

	public Task ResetAsync<T>(CancellationToken cancellationToken = default)
		where T : class, ISettingsSection, new() => UpdateAsync<T>(_ => new T(), cancellationToken);

	public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
