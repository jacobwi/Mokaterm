using Mokaterm.Abstractions.Commands;

namespace Mokaterm.Abstractions.Fleet;

/// <summary>
/// One login of a survey: the login's row and the last summary read from its machine. A later attempt that failed,
/// was skipped or is still going keeps that summary, and <see cref="ReadAt"/> says how old it is.
/// </summary>
public sealed record HostSurveyResult : HostRunResult
{
	public HostSummary? Summary { get; init; }

	/// <summary>When <see cref="Summary"/> was read, by this machine's clock.</summary>
	public DateTimeOffset? ReadAt { get; init; }
}
