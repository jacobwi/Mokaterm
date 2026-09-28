using FluentFTP;

namespace Mokaterm.Modules.Ftp.FileSystem;

/// <summary>One connection lent out for a transfer. Release it exactly once.</summary>
internal sealed class FtpClientLease
{
	private readonly Func<FtpClientLease, bool, ValueTask> _release;
	private int _released;

	public FtpClientLease(AsyncFtpClient client, bool isShared, Func<FtpClientLease, bool, ValueTask> release)
	{
		Client = client;
		IsShared = isShared;
		_release = release;
	}

	public AsyncFtpClient Client { get; }

	/// <summary>True when this is the browsing connection, lent out because the server refused another connection.</summary>
	public bool IsShared { get; }

	/// <summary>
	/// Hands the connection back. Pass false after a failure part way through a transfer: a pooled connection is then
	/// closed instead of reused, because the server may still send replies for the aborted command.
	/// </summary>
	public ValueTask ReleaseAsync(bool reusable) =>
		Interlocked.Exchange(ref _released, 1) == 0 ? _release(this, reusable) : ValueTask.CompletedTask;
}
