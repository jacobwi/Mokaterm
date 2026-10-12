namespace Mokaterm.Modules.Ssh.Admin.Monitoring;

/// <summary>
/// One polling tick of the overview's live figures: a single sample script, read into the history. A sample that fails
/// keeps what is on screen and says why, and the next tick simply tries again.
/// </summary>
internal sealed class MonitorSampler
{
	private readonly Func<AdminScript, CancellationToken, Task<AdminOutcome>> _read;

	/// <param name="read">Runs a read for the page, which is where root mode and the session's exec channels come in.</param>
	public MonitorSampler(Func<AdminScript, CancellationToken, Task<AdminOutcome>> read)
	{
		ArgumentNullException.ThrowIfNull(read);
		_read = read;
	}

	public MonitorHistory History { get; } = new();

	/// <summary>Why the last sample failed, or null when it went through.</summary>
	public string? Error { get; private set; }

	/// <summary>Takes one sample. False when it failed, in which case <see cref="Error"/> says why.</summary>
	/// <exception cref="OperationCanceledException">The page went away.</exception>
	public async Task<bool> SampleAsync(CancellationToken cancellationToken)
	{
		try
		{
			AdminOutcome outcome = await _read(MonitorCommands.Sample(), cancellationToken);
			History.Add(MonitorCommands.Parse(outcome.Output));
			Error = null;
			return true;
		}
		catch (AdminException ex)
		{
			Error = ex.Message;
			return false;
		}
	}
}
