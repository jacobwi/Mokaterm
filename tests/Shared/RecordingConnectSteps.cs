using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Tests.Shared;

/// <summary>
/// Keeps a provider's step reports the way the session manager does, through <see cref="ConnectStepChain"/>, so a test
/// can read where an attempt got to. Reports may come from any thread.
/// </summary>
internal sealed class RecordingConnectSteps : IConnectSteps
{
	private readonly Lock _lock = new();
	private ConnectStepChain _chain = ConnectStepChain.Empty;
	private int _plans;

	public ConnectStepChain Chain
	{
		get
		{
			lock (_lock)
			{
				return _chain;
			}
		}
	}

	/// <summary>How many times the provider planned its steps.</summary>
	public int Plans
	{
		get
		{
			lock (_lock)
			{
				return _plans;
			}
		}
	}

	/// <summary>The planned step ids, in order.</summary>
	public IReadOnlyList<string> Ids => [.. Chain.Steps.Select(status => status.Step.Id)];

	/// <summary>What the session manager makes of the chain when the attempt ends in a failure.</summary>
	public ConnectStepChain EndedWith(string message) => Chain.FailCurrent(message);

	public ConnectStepState StateOf(string stepId) => Chain.Steps.Single(status => status.Step.Id == stepId).State;

	public void Plan(IReadOnlyList<ConnectStep> steps)
	{
		ConnectStepChain planned = ConnectStepChain.Of(steps);
		lock (_lock)
		{
			_chain = planned;
			_plans++;
		}
	}

	public void Start(string stepId, string? detail = null) => Apply(chain => chain.Start(stepId, detail));

	public void Complete(string stepId) => Apply(chain => chain.Complete(stepId));

	public void Fail(string stepId, string reason) => Apply(chain => chain.Fail(stepId, reason));

	public void Skip(string stepId, string? reason = null) => Apply(chain => chain.Skip(stepId, reason));

	private void Apply(Func<ConnectStepChain, ConnectStepChain> change)
	{
		lock (_lock)
		{
			_chain = change(_chain);
		}
	}
}
