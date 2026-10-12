using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The recent samples of each container's CPU and memory, newest last, for the sparklines: one container in its stats
/// tab, every running one on the stats page. A container missing from a sample has stopped, and its line goes with it.
/// </summary>
internal sealed class DockerStatsHistory
{
	/// <summary>How many samples a line holds: two minutes at one sample every three seconds.</summary>
	public const int Length = 40;

	/// <summary>How often a page samples while it shows.</summary>
	public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(3);

	private readonly Dictionary<string, Series> _series = new(StringComparer.Ordinal);

	/// <summary>Adds one sample of each container and drops the lines of those not in it.</summary>
	public void Record(IReadOnlyList<DockerContainerStats> samples)
	{
		ArgumentNullException.ThrowIfNull(samples);
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (DockerContainerStats sample in samples)
		{
			if (!seen.Add(sample.Id))
			{
				continue;
			}

			if (!_series.TryGetValue(sample.Id, out Series? series))
			{
				series = new Series();
				_series[sample.Id] = series;
			}

			series.Cpu.Add(sample.CpuValue ?? 0);
			series.Memory.Add(sample.MemoryPercentValue ?? 0);
		}

		foreach (string gone in _series.Keys.Where(id => !seen.Contains(id)).ToList())
		{
			_series.Remove(gone);
		}
	}

	/// <summary>CPU samples as docker prints them: 100 is one core.</summary>
	public IReadOnlyList<double> Cpu(string id) => _series.TryGetValue(id, out Series? series) ? series.Cpu : [];

	/// <summary>Memory samples as a share of the limit, 0 to 100.</summary>
	public IReadOnlyList<double> Memory(string id) => _series.TryGetValue(id, out Series? series) ? series.Memory : [];

	// One SampleHistory per line for as long as the container runs: a Sparkline is handed the same instance on every
	// render, which is how it follows the samples added since.
	private sealed class Series
	{
		public SampleHistory Cpu { get; } = new(Length);

		public SampleHistory Memory { get; } = new(Length);
	}
}
