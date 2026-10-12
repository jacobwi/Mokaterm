using Mokaterm.Abstractions.Commands;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>A session's commands that nothing under test should run.</summary>
internal sealed class NoCommands : IRemoteCommandFeature
{
	public Task<RemoteCommandOutcome> RunAsync(RemoteCommandRequest request, CancellationToken cancellationToken) =>
		throw new NotSupportedException();
}
