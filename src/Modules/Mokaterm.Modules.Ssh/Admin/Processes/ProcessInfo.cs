namespace Mokaterm.Modules.Ssh.Admin.Processes;

/// <summary>One process as ps lists it.</summary>
internal sealed record ProcessInfo
{
	public required int Pid { get; init; }

	public string User { get; init; } = "";

	/// <summary>ps's CPU figure: the share of one CPU averaged over the process's life, not the last second.</summary>
	public double? Cpu { get; init; }

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
