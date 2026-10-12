using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Fleet;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.DevHost.Demo.FileSystem;
using Mokaterm.Modules.Ssh.Admin.Survey;

namespace Mokaterm.DevHost.Demo;

/// <summary>
/// A demo connection opened only to run commands: no shell and no file tree, like SSH's own. The fleet overview reads
/// it through the SSH module's own survey, which the demo machines answer with canned output.
/// </summary>
internal sealed class DemoCommandSession : IProtocolSession
{
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly DemoCommandFeature _commands;
	private readonly SshHostSummaryFeature _summary;

	public DemoCommandSession(DemoAccount account, string hostName, TimeProvider time)
	{
		_commands = new DemoCommandFeature(account, hostName, time);
		_summary = new SshHostSummaryFeature(_commands);
	}

	public Task Completion => _completion.Task;

	public TFeature? GetFeature<TFeature>() where TFeature : class =>
		typeof(TFeature) == typeof(IHostSummaryFeature) ? _summary as TFeature : _commands as TFeature;

	public ValueTask DisposeAsync()
	{
		_completion.TrySetResult();
		return ValueTask.CompletedTask;
	}
}
