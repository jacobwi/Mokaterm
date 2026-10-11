using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Mokaterm.Modules.Local.Pty.Windows;

/// <summary>The kernel32 calls a pseudo console, its program and the program's job need.</summary>
[SupportedOSPlatform("windows10.0.17763")]
internal static class ConPtyNative
{
	public const uint ExtendedStartupInfoPresent = 0x0008_0000;

	public const uint CreateUnicodeEnvironment = 0x0000_0400;

	public const uint CreateSuspended = 0x0000_0004;

	/// <summary>STARTF_USESTDHANDLES.</summary>
	public const int UseStandardHandles = 0x0000_0100;

	/// <summary>PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE.</summary>
	public const nint PseudoConsoleAttribute = 0x0002_0016;

	/// <summary>The JobObjectExtendedLimitInformation information class.</summary>
	public const int JobObjectExtendedLimitInformation = 9;

	/// <summary>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE.</summary>
	public const uint KillOnJobClose = 0x0000_2000;

	/// <summary>What ResumeThread returns when it fails.</summary>
	public const uint ResumeThreadFailed = 0xFFFF_FFFF;

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, nint pipeAttributes, uint size);

	[DllImport("kernel32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	public static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out SafePseudoConsoleHandle console);

	[DllImport("kernel32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	public static extern int ResizePseudoConsole(SafePseudoConsoleHandle console, Coord size);

	[DllImport("kernel32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	public static extern void ClosePseudoConsole(nint console);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool InitializeProcThreadAttributeList(nint attributeList, int attributeCount, int flags, ref nint size);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool UpdateProcThreadAttribute(
		nint attributeList,
		uint flags,
		nint attribute,
		nint value,
		nint size,
		nint previousValue,
		nint returnSize);

	[DllImport("kernel32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	public static extern void DeleteProcThreadAttributeList(nint attributeList);

	/// <summary>
	/// The command line is a writable buffer, as CreateProcessW requires. The environment is a Unicode block, which is
	/// why <see cref="CreateUnicodeEnvironment"/> goes with it.
	/// </summary>
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool CreateProcessW(
		string applicationName,
		[In, Out] char[] commandLine,
		nint processAttributes,
		nint threadAttributes,
		[MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
		uint creationFlags,
		char[] environment,
		string currentDirectory,
		ref StartupInfoEx startupInfo,
		out ProcessInformation processInformation);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	public static extern SafeKernelHandle CreateJobObjectW(nint jobAttributes, string? name);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool SetInformationJobObject(SafeKernelHandle job, int informationClass, ref JobLimits information, int length);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool AssignProcessToJobObject(SafeKernelHandle job, SafeProcessHandle process);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	public static extern uint ResumeThread(SafeKernelHandle thread);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool CloseHandle(nint handle);
}
