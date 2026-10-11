using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mokaterm.Modules.Local.Pty.Windows;

/// <summary>
/// STARTUPINFOEXW, with STARTUPINFOW's fields written out in place: sequential layout gives the same offsets as the
/// nested struct, and only the size, the flags and the attribute list are ever set.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[SupportedOSPlatform("windows10.0.17763")]
internal record struct StartupInfoEx
{
	public int Size;
	public nint Reserved;
	public nint Desktop;
	public nint Title;
	public int X;
	public int Y;
	public int XSize;
	public int YSize;
	public int XCountChars;
	public int YCountChars;
	public int FillAttribute;
	public int Flags;
	public short ShowWindow;
	public short Reserved2Size;
	public nint Reserved2;
	public nint StandardInput;
	public nint StandardOutput;
	public nint StandardError;
	public nint AttributeList;

	/// <summary>
	/// Startup information that hands the program <paramref name="attributeList"/>. STARTF_USESTDHANDLES with no handles
	/// is deliberate: without it a program started by a process whose own output is redirected (a test runner, a
	/// service) writes to those handles instead of into the pseudo console.
	/// </summary>
	public StartupInfoEx(nint attributeList)
	{
		Size = Marshal.SizeOf<StartupInfoEx>();
		Reserved = 0;
		Desktop = 0;
		Title = 0;
		X = 0;
		Y = 0;
		XSize = 0;
		YSize = 0;
		XCountChars = 0;
		YCountChars = 0;
		FillAttribute = 0;
		Flags = ConPtyNative.UseStandardHandles;
		ShowWindow = 0;
		Reserved2Size = 0;
		Reserved2 = 0;
		StandardInput = 0;
		StandardOutput = 0;
		StandardError = 0;
		AttributeList = attributeList;
	}
}
