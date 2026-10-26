namespace Mokaterm.DevHost.Demo.Docker;

internal sealed class DemoCrashLoop
{
	public required TimeSpan RunsFor { get; init; }

	public required TimeSpan WaitsFor { get; init; }

	public required int ExitCode { get; init; }

	public bool OomKilled { get; init; }

	public required DateTimeOffset Origin { get; set; }

	public int RestartsBefore { get; set; }

	public DateTimeOffset? EndsAt { get; set; }

	public TimeSpan Period => RunsFor + WaitsFor;

	public long RunAt(DateTimeOffset time) => (long)Math.Floor((time - Origin) / Period);

	public DateTimeOffset StartOf(long run) => Origin + (Period * run);
}
