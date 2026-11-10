namespace Mokaterm.UI.FileBrowser.Tests.Fakes;

/// <summary>A clock whose every timer is due at once, so a static render waits out a delay without the wait.</summary>
internal sealed class ImmediateTime : TimeProvider
{
	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		ThreadPool.QueueUserWorkItem(_ => callback(state));
		return new SpentTimer();
	}

	private sealed class SpentTimer : ITimer
	{
		public bool Change(TimeSpan dueTime, TimeSpan period) => false;

		public void Dispose()
		{
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
