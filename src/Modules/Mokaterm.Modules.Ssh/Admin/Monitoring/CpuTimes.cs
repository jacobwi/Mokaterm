using System.Globalization;

namespace Mokaterm.Modules.Ssh.Admin.Monitoring;

/// <summary>
/// The <c>cpu</c> line of <c>/proc/stat</c>: clock ticks every CPU together spent in each state since boot. Only the
/// difference between two readings means anything.
/// </summary>
/// <remarks>Guest time is already counted in user and nice, so it is left out of <see cref="Total"/>.</remarks>
internal readonly record struct CpuTimes(long User, long Nice, long System, long Idle, long IoWait, long Irq, long SoftIrq, long Steal)
{
	public long Total => User + Nice + System + Idle + IoWait + Irq + SoftIrq + Steal;

	/// <summary>
	/// <c>cpu  user nice system idle [iowait irq softirq [steal [guest guest_nice]]]</c>. Kernels before 2.6 print the
	/// first four only and later ones add fields at the end, so missing ones count as zero. Null for any other line.
	/// </summary>
	public static CpuTimes? Parse(string? line)
	{
		string[] words = (line ?? "").Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
		if (words.Length < 5 || words[0] != "cpu")
		{
			return null;
		}

		long[] values = new long[8];
		for (int index = 0; index < values.Length && index + 1 < words.Length; index++)
		{
			if (!long.TryParse(words[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out values[index]))
			{
				return null;
			}
		}

		return new CpuTimes(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]);
	}
}
