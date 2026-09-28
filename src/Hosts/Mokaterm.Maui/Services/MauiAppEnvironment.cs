using Mokaterm.Abstractions.Platform;

namespace Mokaterm.Maui.Services;

internal sealed class MauiAppEnvironment : IAppEnvironment
{
	public MauiAppEnvironment()
	{
		Directory.CreateDirectory(DataDirectory);
		Directory.CreateDirectory(TempDirectory);
	}

	/// <summary>
	/// The build version as the About page and the status bar show it. Not AppInfo.VersionString: MAUI appends
	/// ApplicationVersion to it (0.1.0.1), so the desktop would show another number than the web host for the same build.
	/// </summary>
	public static string Version { get; } = DisplayVersion.Of(typeof(MauiAppEnvironment).Assembly);

	public HostKind Kind => HostKind.Desktop;

	public string PlatformName =>
		OperatingSystem.IsWindows() ? "Windows"
		: OperatingSystem.IsMacCatalyst() ? "macOS"
		: DeviceInfo.Current.Platform.ToString();

	public string AppVersion => Version;

	public string DataDirectory => DesktopPaths.DataDirectory;

	public string TempDirectory => DesktopPaths.TempDirectory;
}
