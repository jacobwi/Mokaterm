using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Abstractions.Fleet;

/// <summary>Saved logins to read for their machine's summary.</summary>
public sealed record HostSurveyRequest
{
	/// <summary>The saved logins, in the order their rows appear. A login listed twice is read once.</summary>
	public required IReadOnlyList<Guid> ConnectionIds { get; init; }

	/// <summary>
	/// The parallel limit, the time per host and the prompt rule, the same ones a command run uses; the output cap is
	/// the summary feature's own. The runner applies <see cref="CommandRunSettings.Clamped"/>.
	/// </summary>
	public CommandRunSettings Options { get; init; } = new();
}
