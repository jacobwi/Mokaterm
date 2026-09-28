using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Rdp.Components;

/// <summary>Settings page for <see cref="RdpSettings"/>. Values outside the supported range are clamped when saved.</summary>
public partial class RdpSettingsPage : SettingsSectionBase<RdpSettings>
{
	private Task SaveAsync(Func<RdpSettings, RdpSettings> change) => UpdateAsync(settings => change(settings).Clamped());
}
