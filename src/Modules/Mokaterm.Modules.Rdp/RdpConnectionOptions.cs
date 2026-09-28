using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Rdp;

/// <summary>Typed access to the <c>rdp.*</c> keys an RDP connection keeps in its <see cref="ProtocolOptions"/>.</summary>
public sealed record RdpConnectionOptions
{
	public const string NetworkLevelAuthenticationKey = "rdp.nla";

	public const string DomainKey = "rdp.domain";

	public const string DesktopSizeKey = "rdp.desktopSize";

	public const string WidthKey = "rdp.width";

	public const string HeightKey = "rdp.height";

	public const string ScalingKey = "rdp.scaling";

	public const string ViewOnlyKey = "rdp.viewOnly";

	public const string KeyboardLayoutKey = "rdp.keyboardLayout";

	public const string WallpaperKey = "rdp.wallpaper";

	public const string ThemesKey = "rdp.themes";

	public const string FontSmoothingKey = "rdp.fontSmoothing";

	public const string FullWindowDragKey = "rdp.fullWindowDrag";

	/// <summary>The smallest desktop RDP negotiates.</summary>
	public const int MinDesktopSize = 200;

	/// <summary>The largest desktop a single monitor can have in RDP, which is what this module connects as.</summary>
	public const int MaxDesktopSize = 8192;

	public const int DefaultWidth = 1920;

	public const int DefaultHeight = 1080;

	public static RdpConnectionOptions Default { get; } = new();

	/// <summary>
	/// CredSSP: the login is checked before the desktop starts. Off sends no credentials at all, so the server
	/// shows its own login screen, which is the only way in when the account cannot be checked up front.
	/// </summary>
	public bool NetworkLevelAuthentication { get; init; } = true;

	/// <summary>The Windows domain or the machine name, empty for a local account or a name that already carries one.</summary>
	public string Domain { get; init; } = "";

	public RdpDesktopSizeMode DesktopSize { get; init; } = RdpDesktopSizeMode.Automatic;

	/// <summary>Width asked for at connect time. Automatic sessions start here and resize once the view is measured.</summary>
	public int Width { get; init; } = DefaultWidth;

	/// <summary>Height asked for at connect time.</summary>
	public int Height { get; init; } = DefaultHeight;

	public RdpScalingMode Scaling { get; init; } = RdpScalingMode.Fit;

	/// <summary>Watch without sending keys or mouse events. The view can still turn input back on.</summary>
	public bool ViewOnly { get; init; }

	/// <summary>A Windows layout id such as 0x0409, or <see cref="RdpKeyboardLayout.ServerDefault"/>.</summary>
	public int KeyboardLayout { get; init; } = RdpKeyboardLayout.ServerDefault;

	public RdpPerformanceOptions Performance { get; init; } = RdpPerformanceOptions.Default;

	public static RdpConnectionOptions From(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		RdpPerformanceOptions defaults = RdpPerformanceOptions.Default;
		return new RdpConnectionOptions
		{
			NetworkLevelAuthentication = options.GetBoolean(NetworkLevelAuthenticationKey, true),
			Domain = options.GetString(DomainKey, ""),
			DesktopSize = options.GetEnum(DesktopSizeKey, RdpDesktopSizeMode.Automatic),
			Width = ClampSize(options.GetInt32(WidthKey, DefaultWidth)),
			Height = ClampSize(options.GetInt32(HeightKey, DefaultHeight)),
			Scaling = options.GetEnum(ScalingKey, RdpScalingMode.Fit),
			ViewOnly = options.GetBoolean(ViewOnlyKey, false),
			KeyboardLayout = ClampLayout(options.GetInt32(KeyboardLayoutKey, RdpKeyboardLayout.ServerDefault)),
			Performance = new RdpPerformanceOptions
			{
				Wallpaper = options.GetBoolean(WallpaperKey, defaults.Wallpaper),
				Themes = options.GetBoolean(ThemesKey, defaults.Themes),
				FontSmoothing = options.GetBoolean(FontSmoothingKey, defaults.FontSmoothing),
				FullWindowDrag = options.GetBoolean(FullWindowDragKey, defaults.FullWindowDrag),
			},
		};
	}

	/// <summary>Writes these values over <paramref name="options"/>, keeping keys that belong to anything else.</summary>
	public ProtocolOptions ApplyTo(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		RdpPerformanceOptions defaults = RdpPerformanceOptions.Default;

		// Keys equal to the default are removed so a saved connection only carries what was actually changed.
		return options
			.With(NetworkLevelAuthenticationKey, NetworkLevelAuthentication ? null : "false")
			.With(DomainKey, Domain.Trim())
			.WithEnum<RdpDesktopSizeMode>(DesktopSizeKey, DesktopSize)
			.With(WidthKey, ClampSize(Width))
			.With(HeightKey, ClampSize(Height))
			.WithEnum<RdpScalingMode>(ScalingKey, Scaling)
			.With(ViewOnlyKey, ViewOnly ? "true" : null)
			.With(KeyboardLayoutKey, KeyboardLayout == RdpKeyboardLayout.ServerDefault ? null : ClampLayout(KeyboardLayout))
			.With(WallpaperKey, Flag(Performance.Wallpaper, defaults.Wallpaper))
			.With(ThemesKey, Flag(Performance.Themes, defaults.Themes))
			.With(FontSmoothingKey, Flag(Performance.FontSmoothing, defaults.FontSmoothing))
			.With(FullWindowDragKey, Flag(Performance.FullWindowDrag, defaults.FullWindowDrag));
	}

	private static string? Flag(bool value, bool fallback) => value == fallback ? null : value ? "true" : "false";

	private static int ClampSize(int value) => Math.Clamp(value, MinDesktopSize, MaxDesktopSize);

	// Layout ids are the low word of a KLID; anything else would be rejected by the server.
	private static int ClampLayout(int value) => value is >= 0 and <= 0xFFFF ? value : RdpKeyboardLayout.ServerDefault;
}
