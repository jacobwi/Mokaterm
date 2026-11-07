namespace Mokaterm.Abstractions.Protocols;

/// <summary>
/// Reports a connect attempt step by step, so the shell can draw where it broke instead of only a status line. Handed to
/// a provider through <see cref="ProtocolConnectContext.Steps"/>; a provider that reports nothing keeps the plain status
/// line. Reports may come from any thread, and one that names a step the plan does not hold is ignored.
/// <see cref="ConnectStepChain"/> holds the rules each report follows.
/// </summary>
public interface IConnectSteps
{
	/// <summary>
	/// The steps this attempt goes through, in order, all pending. Replaces whatever was planned before, so a provider
	/// that starts over (another login after a refused one) plans again.
	/// </summary>
	/// <exception cref="ArgumentException">A step has no id, or two steps share one.</exception>
	void Plan(IReadOnlyList<ConnectStep> steps);

	/// <summary>
	/// <paramref name="stepId"/> is under way. Every step before it held, unless it was skipped, and every step after it
	/// is pending again, so going back to an earlier step (a second dial after a certificate prompt) needs nothing else.
	/// Starting the step that already runs only replaces its <paramref name="detail"/>.
	/// </summary>
	/// <param name="detail">What the step is doing, such as the address being dialed.</param>
	void Start(string stepId, string? detail = null);

	/// <summary><paramref name="stepId"/> finished, and so did every step before it.</summary>
	void Complete(string stepId);

	/// <summary>
	/// The attempt broke at <paramref name="stepId"/>. Only needed where the step under way is not the one to blame, such
	/// as a key that cannot be read before anything is dialed, or to show a refused login while the user is asked for
	/// another: when a connect ends in a <see cref="ProtocolConnectException"/>, the session manager marks the step
	/// under way with the exception's message itself.
	/// </summary>
	void Fail(string stepId, string reason);

	/// <summary><paramref name="stepId"/> does not apply to this attempt, such as a login check the server chose not to run.</summary>
	void Skip(string stepId, string? reason = null);
}
