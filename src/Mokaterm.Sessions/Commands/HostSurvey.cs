using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Fleet;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Sessions.Commands;

/// <summary>
/// Several logins read for their machine's summary. A row keeps the last summary it read through later attempts, so a
/// refresh that fails or is skipped still shows what the machine looked like, with the time it was read.
/// </summary>
internal sealed class HostSurvey : HostRun<HostSurveyResult>, IHostSurvey
{
	public HostSurvey(HostRunner owner, CommandRunSettings options)
		: base(owner, options)
	{
	}

	protected override string RunName => "survey";

	protected override string FailedWithoutReason => "The machine could not be read.";

	protected override string UnexpectedFailure => "The machine could not be read because of an unexpected error.";

	public Task<int> RefreshAsync(IReadOnlyCollection<Guid> connectionIds, bool askPrompts = false, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(connectionIds);
		HashSet<Guid> wanted = [.. connectionIds];
		return RequeueAsync(host => wanted.Contains(host.ConnectionId), wanted, skipPrompts: !askPrompts && Options.SkipHostsThatAsk, cancellationToken);
	}

	protected override string? WhyNot(ProtocolDescriptor? protocol) => HostSurveyEligibility.WhyNot(protocol);

	protected override HostSurveyResult Blank(Guid connectionId) => new()
	{
		ConnectionId = connectionId,
		Title = DeletedLogin,
		HostName = "",
		Address = "",
	};

	protected override HostSurveyResult WithoutResult(HostSurveyResult row) => row;

	protected override string TimedOutWhileRunning(string seconds) => $"Reading the machine did not finish within {seconds} s.";

	protected override async Task<HostSurveyResult> RunOnSessionAsync(IProtocolSession session, Func<HostSurveyResult> started, CancellationToken cancellationToken)
	{
		IHostSummaryFeature summaries = session.GetFeature<IHostSummaryFeature>()
			?? throw new RemoteCommandException("This connection cannot report on its machine.");

		HostSurveyResult row = started();
		long startedAt = Time.GetTimestamp();
		HostSummary summary = await summaries.ReadSummaryAsync(cancellationToken);
		return row with { Summary = summary, ReadAt = Time.GetUtcNow(), Duration = Time.GetElapsedTime(startedAt) };
	}
}
