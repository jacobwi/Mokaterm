using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Ftp.Components;

/// <summary>Settings page for <see cref="FtpSettings"/>. Values outside the supported range are clamped when saved.</summary>
public partial class FtpSettingsPage : SettingsSectionBase<FtpSettings>
{
	private Task SaveAsync(Func<FtpSettings, FtpSettings> change) => UpdateAsync(settings => change(settings).Clamped());
}
