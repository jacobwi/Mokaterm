using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Mokaterm.Modules.Local.Pty.Windows;

/// <summary>A kernel handle released with CloseHandle: the job a shell runs in, and its program's first thread.</summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
{
	public SafeKernelHandle()
		: base(ownsHandle: true)
	{
	}

	public SafeKernelHandle(nint existing)
		: base(ownsHandle: true)
	{
		SetHandle(existing);
	}

	protected override bool ReleaseHandle() => ConPtyNative.CloseHandle(handle);
}
