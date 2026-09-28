using System.Runtime.Versioning;

namespace Mokaterm.Modules.Serial.Protocol;

/// <summary>
/// Whether this machine has serial ports at all. <c>System.IO.Ports</c> is unsupported on Android, iOS, tvOS and
/// in the browser, and the module targets plain .NET, so every call into it sits behind this guard. The attributes
/// are what tells the platform compatibility analyzer that an <c>if</c> over it is a real check.
/// </summary>
internal static class SerialPlatform
{
	[SupportedOSPlatformGuard("windows")]
	[SupportedOSPlatformGuard("linux")]
	[SupportedOSPlatformGuard("macos")]
	public static bool IsSupported =>
		OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

	/// <summary>The message a session fails with on a platform that has no serial ports.</summary>
	public const string Unsupported = "Serial ports are not available on this operating system.";
}
