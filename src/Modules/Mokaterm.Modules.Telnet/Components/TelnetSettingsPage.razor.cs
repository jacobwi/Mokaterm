using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Telnet.Components;

/// <summary>Settings page for <see cref="TelnetSettings"/>. Values outside the supported range are clamped when saved.</summary>
public partial class TelnetSettingsPage : SettingsSectionBase<TelnetSettings>
{
	private Task SaveAsync(Func<TelnetSettings, TelnetSettings> change) => UpdateAsync(settings => change(settings).Clamped());
}
