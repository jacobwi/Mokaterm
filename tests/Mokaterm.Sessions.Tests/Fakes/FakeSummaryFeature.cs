using Mokaterm.Abstractions.Fleet;

namespace Mokaterm.Sessions.Tests.Fakes;

/// <summary>Reads a machine the way a test says, counting how many reads run at once.</summary>
internal sealed class FakeSummaryFeature : IHostSummaryFeature
{
	private int _reads;
	private int _running;

	public Func<CancellationToken, Task<HostSummary>> OnRead { get; set; } =
		_ => Task.FromResult(new HostSummary { OperatingSystem = "Ubuntu 24.04.1 LTS", Uptime = TimeSpan.FromDays(3) });

	public int Reads => Volatile.Read(ref _reads);

	public int Running => Volatile.Read(ref _running);

	public async Task<HostSummary> ReadSummaryAsync(CancellationToken cancellationToken)
	{
		_ = Interlocked.Increment(ref _reads);
		_ = Interlocked.Increment(ref _running);
		try
		{
			return await OnRead(cancellationToken);
		}
		finally
		{
			_ = Interlocked.Decrement(ref _running);
		}
	}
}
