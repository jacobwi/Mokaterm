namespace Mokaterm.Abstractions.Settings;

/// <summary>
/// A settings section: an immutable record stored under <see cref="SectionKey"/> in <c>settings.json</c>.
/// Modules add their own sections the same way the built-in ones are declared.
/// </summary>
public interface ISettingsSection
{
	/// <summary>Stable lowercase key, for example <c>terminal</c> or <c>ssh</c>.</summary>
	static abstract string SectionKey { get; }
}

/// <summary>
/// App-wide settings. Loaded before the vault unlocks so the lock screen can be themed; never holds secrets.
/// Sections the running app does not know (from a module that is not loaded) are kept when saving.
/// </summary>
public interface ISettingsService
{
	/// <summary>Raised with the section key after a section changes. Handlers may run on any thread.</summary>
	event Action<string>? Changed;

	Task LoadAsync(CancellationToken cancellationToken = default);

	/// <summary>The current section, or its defaults when nothing is saved.</summary>
	T Get<T>() where T : class, ISettingsSection, new();

	/// <summary>
	/// Replaces a section with the result of <paramref name="update"/>. Readers see the new value at once;
	/// the file write is debounced so sliders do not rewrite the file on every tick.
	/// </summary>
	Task UpdateAsync<T>(Func<T, T> update, CancellationToken cancellationToken = default) where T : class, ISettingsSection, new();

	Task ResetAsync<T>(CancellationToken cancellationToken = default) where T : class, ISettingsSection, new();

	/// <summary>Writes pending changes now. Hosts call this on shutdown.</summary>
	Task FlushAsync(CancellationToken cancellationToken = default);
}
