using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// Base for a settings page bound to one section. Re-renders when the section changes anywhere, including
/// another window or browser tab.
/// </summary>
public abstract class SettingsSectionBase<TSettings> : ComponentBase, IDisposable
	where TSettings : class, ISettingsSection, new()
{
	private bool _disposed;

	[Inject]
	protected ISettingsService SettingsService { get; set; } = default!;

	protected TSettings Settings => SettingsService.Get<TSettings>();

	protected override void OnInitialized() => SettingsService.Changed += OnSettingsChanged;

	protected Task UpdateAsync(Func<TSettings, TSettings> update) => SettingsService.UpdateAsync(update);

	protected Task ResetAsync() => SettingsService.ResetAsync<TSettings>();

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_disposed)
		{
			return;
		}

		if (disposing)
		{
			SettingsService.Changed -= OnSettingsChanged;
		}

		_disposed = true;
	}

	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey == TSettings.SectionKey)
		{
			_ = InvokeAsync(StateHasChanged);
		}
	}
}
