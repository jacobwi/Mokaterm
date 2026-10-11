using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Mokaterm.Modules.Local.Pty.Windows;

/// <summary>
/// A process handle the thread pool can wait on, which is signalled when the process exits. It holds a reference on
/// the process handle until it is disposed: a handle closed while a wait on it is pending is undefined behaviour, so the
/// process handle cannot really close before this does, whoever disposes it first.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class ProcessExitWaitHandle : WaitHandle
{
	private readonly SafeProcessHandle _process;
	private bool _referenced;

	public ProcessExitWaitHandle(SafeProcessHandle process)
	{
		ArgumentNullException.ThrowIfNull(process);
		_process = process;
		process.DangerousAddRef(ref _referenced);
		SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false);
	}

	protected override void Dispose(bool explicitDisposing)
	{
		base.Dispose(explicitDisposing);
		if (_referenced)
		{
			_referenced = false;
			_process.DangerousRelease();
		}
	}
}
