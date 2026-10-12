namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>One exec channel's place in its connection's <see cref="ExecChannelBudget"/>, given back once on dispose.</summary>
internal sealed class ExecSlot : IDisposable
{
	private readonly ExecChannelBudget _budget;
	private readonly bool _runsLong;
	private int _released;

	internal ExecSlot(ExecChannelBudget budget, bool runsLong)
	{
		_budget = budget;
		_runsLong = runsLong;
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _released, 1) == 0)
		{
			_budget.Release(_runsLong);
		}
	}
}
