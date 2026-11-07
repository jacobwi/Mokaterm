using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Sessions;

/// <summary>
/// The step reports of one connect attempt. Each one goes to the handle tagged with the attempt, the way status lines
/// do, so an older attempt or one that already ended cannot redraw the chain.
/// </summary>
internal sealed class AttemptConnectSteps(SessionHandle handle, int attempt) : IConnectSteps
{
	public void Plan(IReadOnlyList<ConnectStep> steps)
	{
		// Checked here so a provider's broken plan throws back at the provider, outside the handle's lock.
		ConnectStepChain planned = ConnectStepChain.Of(steps);
		handle.ReportSteps(attempt, _ => planned);
	}

	public void Start(string stepId, string? detail = null) => handle.ReportSteps(attempt, chain => chain.Start(stepId, detail));

	public void Complete(string stepId) => handle.ReportSteps(attempt, chain => chain.Complete(stepId));

	public void Fail(string stepId, string reason) => handle.ReportSteps(attempt, chain => chain.Fail(stepId, reason));

	public void Skip(string stepId, string? reason = null) => handle.ReportSteps(attempt, chain => chain.Skip(stepId, reason));
}
