using System.Globalization;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Rdp.Components;

/// <summary>The RDP part of the connection editor: the login check, the desktop size, scaling and what the server draws.</summary>
public partial class RdpOptionsEditor : ConnectionOptionsEditorBase<RdpConnectionOptions>
{
	private static readonly string SizeHelp = string.Create(
		CultureInfo.CurrentCulture,
		$"{RdpConnectionOptions.MinDesktopSize} to {RdpConnectionOptions.MaxDesktopSize} pixels.");

	private const string PerformanceHelp = "Turning these off is what makes a session over a slow link feel quick.";

	/// <summary>The list entry for the stored layout id, or one named after the id when the list has none.</summary>
	private RdpKeyboardLayout Layout => RdpKeyboardLayout.For(Current.KeyboardLayout);

	private string NlaHelp => Current.NetworkLevelAuthentication
		? "The login is checked before the desktop starts, which is what every current Windows server expects."
		: "No credentials are sent and the server shows its own login screen. Older servers and some gateways need this.";

	private string DesktopSizeHelp => Current.DesktopSize == RdpDesktopSizeMode.Fixed
		? "Always asks for this size, whatever the view measures."
		: "Starts at 1920 by 1080 and asks the server to match the view once it opens. Servers that cannot resize stay as they are.";

	private string ScalingHelp => Current.Scaling switch
	{
		RdpScalingMode.Actual => "Shows every remote pixel and scrolls when the desktop is larger than the view.",
		RdpScalingMode.Remote => "Asks the server to match its desktop to the view whenever the view changes size.",
		_ => "Scales the whole desktop into the view, which keeps the picture complete but not pixel exact.",
	};

	protected override RdpConnectionOptions From(ProtocolOptions options) => RdpConnectionOptions.From(options);

	protected override ProtocolOptions ApplyTo(RdpConnectionOptions current, ProtocolOptions options) => current.ApplyTo(options);

	private Task OnNlaChangedAsync(bool value) => ChangeAsync(Current with { NetworkLevelAuthentication = value });

	private Task OnDomainChangedAsync(string value) => ChangeAsync(Current with { Domain = value ?? "" });

	private Task OnDesktopSizeChangedAsync(string value) =>
		Enum.TryParse(value, out RdpDesktopSizeMode mode) && Enum.IsDefined(mode)
			? ChangeAsync(Current with { DesktopSize = mode })
			: Task.CompletedTask;

	private Task OnWidthChangedAsync(int value) => ChangeAsync(Current with { Width = Clamp(value) });

	private Task OnHeightChangedAsync(int value) => ChangeAsync(Current with { Height = Clamp(value) });

	private Task OnScalingChangedAsync(string value) =>
		Enum.TryParse(value, out RdpScalingMode scaling) && Enum.IsDefined(scaling)
			? ChangeAsync(Current with { Scaling = scaling })
			: Task.CompletedTask;

	private Task OnLayoutChangedAsync(RdpKeyboardLayout? layout) =>
		ChangeAsync(Current with { KeyboardLayout = layout?.Id ?? RdpKeyboardLayout.ServerDefault });

	private Task OnViewOnlyChangedAsync(bool value) => ChangeAsync(Current with { ViewOnly = value });

	private Task OnWallpaperChangedAsync(bool value) =>
		ChangeAsync(Current with { Performance = Current.Performance with { Wallpaper = value } });

	private Task OnThemesChangedAsync(bool value) =>
		ChangeAsync(Current with { Performance = Current.Performance with { Themes = value } });

	private Task OnFontSmoothingChangedAsync(bool value) =>
		ChangeAsync(Current with { Performance = Current.Performance with { FontSmoothing = value } });

	private Task OnFullWindowDragChangedAsync(bool value) =>
		ChangeAsync(Current with { Performance = Current.Performance with { FullWindowDrag = value } });

	// MokaNumericField does not apply Min and Max, so the size is kept in range here.
	private static int Clamp(int value) => Math.Clamp(value, RdpConnectionOptions.MinDesktopSize, RdpConnectionOptions.MaxDesktopSize);
}
