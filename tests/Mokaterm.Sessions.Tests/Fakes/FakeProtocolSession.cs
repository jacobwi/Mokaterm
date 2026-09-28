using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Sessions.Tests.Fakes;

internal sealed class FakeProtocolSession : IProtocolSession
{
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _disposeCount;

	public FakeProtocolSession(FakeTerminalChannel? channel = null) => Channel = channel;

	public FakeTerminalChannel? Channel { get; }

	public Task Completion => _completion.Task;

	public bool IsDisposed => Volatile.Read(ref _disposeCount) > 0;

	public int DisposeCount => Volatile.Read(ref _disposeCount);

	/// <summary>When set, disposing waits for this task, like a library stuck closing a dead socket.</summary>
	public Task? DisposeBlocker { get; init; }

	public TFeature? GetFeature<TFeature>() where TFeature : class => Channel as TFeature;

	/// <summary>The remote side ended the session normally, for example after <c>exit</c>.</summary>
	public void EndCleanly()
	{
		Channel?.Close();
		_ = _completion.TrySetResult();
	}

	/// <summary>The connection dropped.</summary>
	public void Drop(Exception error)
	{
		Channel?.Close();
		_ = _completion.TrySetException(error);
	}

	public async ValueTask DisposeAsync()
	{
		_ = Interlocked.Increment(ref _disposeCount);
		Channel?.Close();
		_ = _completion.TrySetResult();
		if (DisposeBlocker is not null)
		{
			await DisposeBlocker;
		}
	}
}
