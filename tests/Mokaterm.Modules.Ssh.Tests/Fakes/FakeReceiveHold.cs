using Mokaterm.Modules.Ssh.KeepAlive;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>The shell's flow control as the keepalive loop sees it, held and let go by the test.</summary>
internal sealed class FakeReceiveHold : IReceiveHold
{
	private long _holds;
	private bool _held;

	public long Holds => Interlocked.Read(ref _holds);

	public bool IsHeld => Volatile.Read(ref _held);

	public void Hold()
	{
		_ = Interlocked.Increment(ref _holds);
		Volatile.Write(ref _held, true);
	}

	public void Release() => Volatile.Write(ref _held, false);
}
