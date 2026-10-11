using System.Runtime.InteropServices;

namespace Mokaterm.Modules.Local.Pty.Windows;

/// <summary>
/// JOBOBJECT_EXTENDED_LIMIT_INFORMATION, with its basic limits and I/O counters written out in place: sequential layout
/// gives the same offsets as the nested structs. Only the limit flags are ever set.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal record struct JobLimits
{
	public long PerProcessUserTimeLimit;
	public long PerJobUserTimeLimit;
	public uint LimitFlags;
	public nuint MinimumWorkingSetSize;
	public nuint MaximumWorkingSetSize;
	public uint ActiveProcessLimit;
	public nuint Affinity;
	public uint PriorityClass;
	public uint SchedulingClass;
	public ulong ReadOperationCount;
	public ulong WriteOperationCount;
	public ulong OtherOperationCount;
	public ulong ReadTransferCount;
	public ulong WriteTransferCount;
	public ulong OtherTransferCount;
	public nuint ProcessMemoryLimit;
	public nuint JobMemoryLimit;
	public nuint PeakProcessMemoryUsed;
	public nuint PeakJobMemoryUsed;

	public JobLimits(uint limitFlags)
	{
		PerProcessUserTimeLimit = 0;
		PerJobUserTimeLimit = 0;
		LimitFlags = limitFlags;
		MinimumWorkingSetSize = 0;
		MaximumWorkingSetSize = 0;
		ActiveProcessLimit = 0;
		Affinity = 0;
		PriorityClass = 0;
		SchedulingClass = 0;
		ReadOperationCount = 0;
		WriteOperationCount = 0;
		OtherOperationCount = 0;
		ReadTransferCount = 0;
		WriteTransferCount = 0;
		OtherTransferCount = 0;
		ProcessMemoryLimit = 0;
		JobMemoryLimit = 0;
		PeakProcessMemoryUsed = 0;
		PeakJobMemoryUsed = 0;
	}
}
