using System.Runtime.Versioning;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Local.Pty.Windows;

namespace Mokaterm.Modules.Local.Pty;

/// <summary>
/// Whether this computer can run a shell in a pseudo terminal, and the one place that picks how. The attribute is what
/// tells the platform compatibility analyzer that an <c>if</c> over <see cref="IsSupported"/> is a real check.
/// </summary>
internal static class PseudoTerminalPlatform
{
	/// <summary>The message a connect fails with where there is no pseudo terminal to run a shell in.</summary>
	public const string Unsupported =
		"Local shells need the Windows pseudo console, which arrived in Windows 10 version 1809. Other systems are not supported yet.";

	/// <summary>ConPTY: Windows 10 version 1809 (build 17763) and later.</summary>
	[SupportedOSPlatformGuard("windows10.0.17763")]
	public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

	/// <summary>Starts <paramref name="launch"/> in a pseudo terminal of <paramref name="size"/>.</summary>
	/// <exception cref="PlatformNotSupportedException">This computer has no pseudo terminal this module can use.</exception>
	public static IPseudoTerminal Start(PseudoTerminalLaunch launch, TerminalSize size, IPseudoTerminalOutput output) =>
		IsSupported ? ConPtyTerminal.Start(launch, size, output) : throw new PlatformNotSupportedException(Unsupported);
}
