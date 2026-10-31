namespace Mokaterm.UI.Common.Components;

/// <summary>
/// The widths a settings row's control takes, so every page offers the same box for the same kind of value instead of
/// the handful of pixel literals each page picked on its own.
/// </summary>
public static class SettingWidths
{
	/// <summary>A number: a port, a timeout, a size. The width NumberSetting already drew.</summary>
	public const string Narrow = "88px";

	/// <summary>A short text: a name, a path fragment, a host.</summary>
	public const string Standard = "240px";

	/// <summary>A long text or a select with long entries: a command, a URL.</summary>
	public const string Wide = "340px";
}
