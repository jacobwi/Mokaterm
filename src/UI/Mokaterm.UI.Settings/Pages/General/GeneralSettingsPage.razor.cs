using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Settings.Pages.General;

/// <summary>Settings page for <see cref="GeneralSettings"/>.</summary>
public sealed partial class GeneralSettingsPage : SettingsSectionBase<GeneralSettings>
{
	private static readonly IReadOnlyList<EnumOption<SessionRestoreMode>> RestoreOptions =
	[
		new(SessionRestoreMode.Off, "Never"),
		new(SessionRestoreMode.Ask, "Ask"),
		new(SessionRestoreMode.Always, "Always"),
	];

	private Task SaveAsync(Func<GeneralSettings, GeneralSettings> change) => UpdateAsync(s => change(s).Clamped());
}
