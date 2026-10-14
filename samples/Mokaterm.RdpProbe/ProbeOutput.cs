using System.Diagnostics;
using System.Globalization;

namespace Mokaterm.RdpProbe;

/// <summary>
/// Writes the probe's lines, each step with the time since the start. The connector's status lines arrive through
/// <see cref="IProgress{T}"/>, so the last one is the step a failure happened in.
/// </summary>
internal sealed class ProbeOutput : IProgress<string>
{
	private const string Indent = "             ";

	private readonly Stopwatch _clock = Stopwatch.StartNew();
	private readonly Lock _gate = new();

	/// <summary>The last step the connector reported.</summary>
	public string? LastStep { get; private set; }

	public TimeSpan Elapsed => _clock.Elapsed;

	public void Report(string value)
	{
		lock (_gate)
		{
			LastStep = value;
			Console.WriteLine($"[{Stamp()}] {value}");
		}
	}

	/// <summary>A line that belongs to the step above it.</summary>
	public void Detail(string text)
	{
		lock (_gate)
		{
			Console.WriteLine(Indent + text);
		}
	}

	/// <summary>A line of its own, with the time.</summary>
	public void Event(string text) => Report(text);

	private string Stamp() => string.Create(CultureInfo.InvariantCulture, $"{_clock.Elapsed.TotalSeconds,8:0.000} s");
}
