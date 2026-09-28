using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.DevHost.Demo.FileSystem;
using Mokaterm.DevHost.Demo.Terminal;
using Mokaterm.Modules.Ssh.Tunnels;

namespace Mokaterm.DevHost.Demo;

/// <summary>
/// One demo connection: a fake shell and a file browser over the same in-memory tree, so a file created in one shows up
/// in the other. Ends cleanly when the login shell exits or the session is disposed.
/// </summary>
internal sealed class DemoSession : IProtocolSession
{
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

	// Never disposed: file system views outlive the session in the browser and keep checking its token.
	private readonly CancellationTokenSource _closed = new();
	private readonly DemoTerminalChannel _terminal;
	private readonly DemoFileSystemFeature _files;
	private readonly DemoTunnels _tunnels;
	private int _disposed;

	public DemoSession(DemoAccount account, string hostName, string address, TerminalSize size, TimeProvider timeProvider, ILogger logger)
	{
		DemoFileTree tree = DemoTreeSeed.Create(account, hostName, timeProvider);
		_files = new DemoFileSystemFeature(tree, account, _closed.Token);
		DemoShell shell = new(tree, account, hostName, address, size, timeProvider);
		_terminal = new DemoTerminalChannel(shell, () => _completion.TrySetResult(), logger);
		_tunnels = new DemoTunnels(timeProvider);
	}

	public Task Completion => _completion.Task;

	public TFeature? GetFeature<TFeature>() where TFeature : class
	{
		if (typeof(TFeature) == typeof(ITerminalChannel))
		{
			return _terminal as TFeature;
		}

		if (typeof(TFeature) == typeof(ISshTunnelFeature))
		{
			return _tunnels as TFeature;
		}

		return typeof(TFeature) == typeof(IFileSystemFeature) ? _files as TFeature : null;
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		// File system views keep the token and fail with "connection lost" from here on.
		_closed.Cancel();
		await _terminal.DisposeAsync();
		await _files.DisposeAsync();
		_completion.TrySetResult();
	}
}
