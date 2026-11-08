using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The recent samples of each container's figures, newest last, for the sparklines and the live charts: one container
/// in its stats tab, every running one on the stats page. A container missing from a sample has stopped, and its lines
/// go with it.
/// </summary>
internal sealed class DockerStatsHistory
{
	/// <summary>How many samples a line holds: two minutes at one sample every three seconds.</summary>
	public const int Length = 40;

	// docker stats prints network and block totals, not rates, and only to three figures (12.4MB), so a rate worked out
	// between two samples would read zero until the printed total moves, then jump. Rates are measured over up to this
	// many samples (thirty seconds) instead, which evens that out; they still read coarser as the totals grow.
	public const int RateSpan = 10;

	/// <summary>How often a page samples while it shows.</summary>
	public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(3);

	// What a container without lines reads as. Nothing ever adds to it.
	private static readonly SampleHistory None = new(1);

	private readonly Dictionary<string, Series> _series = new(StringComparer.Ordinal);

	public IReadOnlyCollection<string> Ids => _series.Keys;

	/// <summary>Adds one sample of each container, taken at <paramref name="at"/>, and drops the lines of those not in it.</summary>
	public void Record(IReadOnlyList<DockerContainerStats> samples, DateTimeOffset at)
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

			series.Add(sample, at);
		}

		foreach (string gone in _series.Keys.Where(id => !seen.Contains(id)).ToList())
		{
			_series.Remove(gone);
		}
	}

	/// <summary>CPU samples as docker prints them: 100 is one core.</summary>
	public IReadOnlyList<double> Cpu(string id) => Line(id, series => series.Cpu);

	/// <summary>Memory samples as a share of the limit, 0 to 100.</summary>
	public IReadOnlyList<double> Memory(string id) => Line(id, series => series.Memory);

	public IReadOnlyList<double> MemoryBytes(string id) => Line(id, series => series.MemoryBytes);

	// Rates are bytes a second, NaN until there are two samples to measure between.
	public IReadOnlyList<double> Received(string id) => Line(id, series => series.Received);

	public IReadOnlyList<double> Sent(string id) => Line(id, series => series.Sent);

	public IReadOnlyList<double> Read(string id) => Line(id, series => series.Read);

	public IReadOnlyList<double> Written(string id) => Line(id, series => series.Written);

	public DockerRates RatesOf(string id) => new(
		Latest(Received(id)),
		Latest(Sent(id)),
		Latest(Read(id)),
		Latest(Written(id)));

	// Lines of different lengths are lined up at their newest sample, since a container that started later has fewer.
	// A slot no line has a number for stays NaN, a gap.
	public static double[] Sum(IEnumerable<IReadOnlyList<double>> lines)
	{
		ArgumentNullException.ThrowIfNull(lines);
		double[] total = new double[Length];
		Array.Fill(total, double.NaN);
		foreach (IReadOnlyList<double> line in lines)
		{
			int offset = Length - line.Count;
			for (int index = Math.Max(0, -offset); index < line.Count; index++)
			{
				double value = line[index];
				if (double.IsFinite(value))
				{
					int slot = offset + index;
					total[slot] = double.IsNaN(total[slot]) ? value : total[slot] + value;
				}
			}
		}

		return total;
	}

	private static double? Latest(IReadOnlyList<double> line) => line.Count > 0 && double.IsFinite(line[^1]) ? line[^1] : null;

	private SampleHistory Line(string id, Func<Series, SampleHistory> pick) =>
		_series.TryGetValue(id, out Series? series) ? pick(series) : None;

	// One SampleHistory per line for as long as the container runs: a chart is handed the same instance on every render,
	// which is how it follows the samples added since.
	private sealed class Series
	{
		private readonly Queue<Totals> _totals = new();

		public SampleHistory Cpu { get; } = new(Length);

		public SampleHistory Memory { get; } = new(Length);

		public SampleHistory MemoryBytes { get; } = new(Length);

		public SampleHistory Received { get; } = new(Length);

		public SampleHistory Sent { get; } = new(Length);

		public SampleHistory Read { get; } = new(Length);

		public SampleHistory Written { get; } = new(Length);

		public void Add(DockerContainerStats sample, DateTimeOffset at)
		{
			Cpu.Add(sample.CpuValue ?? 0);
			Memory.Add(sample.MemoryPercentValue ?? 0);
			MemoryBytes.Add(sample.MemoryBytes is { } bytes ? bytes : double.NaN);

			(long? received, long? sent) = DockerSizes.ParsePair(sample.NetworkIo);
			(long? read, long? written) = DockerSizes.ParsePair(sample.BlockIo);
			Totals now = new(at, received, sent, read, written);

			// A restarted container counts from zero again, so nothing before it measures a rate.
			if (_totals.Count > 0 && GoesBack(_totals.Last(), now))
			{
				_totals.Clear();
			}

			Totals? from = _totals.Count > 0 ? _totals.Peek() : null;
			Received.Add(RateOf(from?.Received, received, from, at));
			Sent.Add(RateOf(from?.Sent, sent, from, at));
			Read.Add(RateOf(from?.Read, read, from, at));
			Written.Add(RateOf(from?.Written, written, from, at));

			_totals.Enqueue(now);
			while (_totals.Count > RateSpan)
			{
				_totals.Dequeue();
			}
		}

		private static bool GoesBack(Totals before, Totals now) =>
			now.Received < before.Received || now.Sent < before.Sent || now.Read < before.Read || now.Written < before.Written;

		private static double RateOf(long? before, long? now, Totals? from, DateTimeOffset at)
		{
			if (before is not { } start || now is not { } end || from is not { } earlier)
			{
				return double.NaN;
			}

			double seconds = (at - earlier.At).TotalSeconds;
			return seconds > 0 && end >= start ? (end - start) / seconds : double.NaN;
		}
	}

	private readonly record struct Totals(DateTimeOffset At, long? Received, long? Sent, long? Read, long? Written);
}
