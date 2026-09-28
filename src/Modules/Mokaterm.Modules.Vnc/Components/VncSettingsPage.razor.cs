using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Vnc.Components;

/// <summary>Settings page for <see cref="VncSettings"/>. Values outside the supported range are clamped when saved.</summary>
public partial class VncSettingsPage : SettingsSectionBase<VncSettings>
{
	private Task SaveAsync(Func<VncSettings, VncSettings> change) => UpdateAsync(settings => change(settings).Clamped());
}
