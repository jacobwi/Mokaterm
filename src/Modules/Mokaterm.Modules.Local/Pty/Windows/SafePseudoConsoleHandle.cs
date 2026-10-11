using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Mokaterm.Modules.Local.Pty.Windows;

/// <summary>
/// An HPCON. Releasing it is ClosePseudoConsole, which can block until conhost's last frame has been read, so
/// <see cref="ConPtyTerminal"/> disposes it itself, on a thread of its own and while its reader is still draining.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class SafePseudoConsoleHandle : SafeHandleZeroOrMinusOneIsInvalid
{
	public SafePseudoConsoleHandle()
		: base(ownsHandle: true)
	{
	}

	protected override bool ReleaseHandle()
	{
		ConPtyNative.ClosePseudoConsole(handle);
		return true;
	}
}
