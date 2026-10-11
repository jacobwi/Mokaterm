using System.Runtime.InteropServices;

namespace Mokaterm.Modules.Local.Pty.Windows;

/// <summary>PROCESS_INFORMATION: what CreateProcessW hands back. Both handles belong to the caller.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct ProcessInformation(nint Process, nint Thread, int ProcessId, int ThreadId);
