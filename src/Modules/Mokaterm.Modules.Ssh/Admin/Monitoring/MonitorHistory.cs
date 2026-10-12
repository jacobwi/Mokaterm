using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Ssh.Admin.Monitoring;

/// <summary>
/// The overview's live figures: the newest sample, the rates since the one before it, and the last
/// <see cref="Capacity"/> values of each figure for its sparkline.
/// </summary>
/// <remarks>
/// Memory, swap and load are recorded from the first sample on; CPU and the rates from the second, because they are
/// differences. A sample that is not newer than the last one by the server's uptime is dropped, so a read that came
/// back late cannot draw a step backwards. A reboot never shows up here: it ends the SSH session, and the view starts
/// over with the next one.
/// </remarks>
internal sealed class MonitorHistory
{
	/// <summary>Two minutes at one sample every two seconds.</summary>
	public const int Capacity = 60;

	public SampleHistory Cpu { get; } = new(Capacity);

	public SampleHistory IoWait { get; } = new(Capacity);

	public SampleHistory Memory { get; } = new(Capacity);

	public SampleHistory Swap { get; } = new(Capacity);

	public SampleHistory Load { get; } = new(Capacity);

	public SampleHistory Received { get; } = new(Capacity);

	public SampleHistory Sent { get; } = new(Capacity);

	public SampleHistory Read { get; } = new(Capacity);

	public SampleHistory Written { get; } = new(Capacity);

	/// <summary>The newest sample kept, or null before the first.</summary>
	public SystemSample? Latest { get; private set; }

	/// <summary>The rates between the two newest samples, or null until there are two.</summary>
	public SystemRates? Rates { get; private set; }

	/// <summary>Records <paramref name="sample"/>. False when it was dropped as older than the newest one.</summary>
	public bool Add(SystemSample sample)
	{
		ArgumentNullException.ThrowIfNull(sample);
		SystemSample? previous = Latest;
		if (previous?.UptimeSeconds is { } before && sample.UptimeSeconds is { } now && now <= before)
		{
			return false;
		}

		Latest = sample;
		Rates = previous is null ? null : SystemRates.Between(previous, sample);
		Memory.Add(sample.Memory.UsedPercent ?? 0);
		Swap.Add(sample.Memory.SwapUsedPercent ?? 0);
		Load.Add(sample.Load?.One ?? 0);
		if (Rates is { } rates)
		{
			if (rates.CpuPercent is { } cpu)
			{
				Cpu.Add(cpu);
				IoWait.Add(rates.IoWaitPercent ?? 0);
			}

			if (rates.Seconds is not null)
			{
				Received.Add(rates.ReceivedPerSecond ?? 0);
				Sent.Add(rates.SentPerSecond ?? 0);
				Read.Add(rates.ReadPerSecond ?? 0);
				Written.Add(rates.WrittenPerSecond ?? 0);
			}
		}

		return true;
	}
}
