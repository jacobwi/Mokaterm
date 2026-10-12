namespace Mokaterm.Modules.Ssh.Admin.Processes;

/// <summary>One process as ps lists it, with its CPU share sampled over a second.</summary>
internal sealed record ProcessInfo
{
	public required int Pid { get; init; }

	/// <summary>The parent's PID from <c>/proc/[pid]/stat</c>, or null when the sample missed the process.</summary>
	public int? ParentPid { get; init; }

	public string User { get; init; } = "";

	/// <summary>
	/// Share of one CPU in percent: measured over the second the list was read when <see cref="CpuSampled"/>, else ps's
	/// average over the process's whole life (a process that started after the sample).
	/// </summary>
	public double? Cpu { get; init; }

	/// <summary><see cref="Cpu"/> was measured over the sampling second rather than averaged over the process's life.</summary>
	public bool CpuSampled { get; init; }

	/// <summary>The nice value, -20 (first in line) to 19 (last), or null when the sample missed the process.</summary>
	public int? Nice { get; init; }

	/// <summary>Resident memory as a share of all memory.</summary>
	public double? Memory { get; init; }

	public long? ResidentKb { get; init; }

	/// <summary>The ps state letters: R running, S sleeping, D waiting on I/O, Z zombie, T stopped.</summary>
	public string State { get; init; } = "";

	/// <summary>The command line, or <c>[name]</c> for a kernel thread.</summary>
	public string Command { get; init; } = "";

	/// <summary>The program's file name without its folder, for sorting and warnings.</summary>
	public string Name
	{
		get
		{
			string first = Command.Split(' ', 2)[0];
			if (first.StartsWith('['))
			{
				return first.Trim('[', ']');
			}

			int slash = first.LastIndexOf('/');
			return (slash >= 0 ? first[(slash + 1)..] : first).TrimEnd(':');
		}
	}
}
