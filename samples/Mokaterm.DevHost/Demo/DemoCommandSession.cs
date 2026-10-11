using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.DevHost.Demo.FileSystem;

namespace Mokaterm.DevHost.Demo;

/// <summary>A demo connection opened only to run commands: no shell and no file tree, like SSH's own.</summary>
internal sealed class DemoCommandSession : IProtocolSession
{
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly DemoCommandFeature _commands;

	public DemoCommandSession(DemoAccount account, string hostName, TimeProvider time) =>
		_commands = new DemoCommandFeature(account, hostName, time);

	public Task Completion => _completion.Task;

	public TFeature? GetFeature<TFeature>() where TFeature : class => _commands as TFeature;

	public ValueTask DisposeAsync()
	{
		_completion.TrySetResult();
		return ValueTask.CompletedTask;
	}
}
