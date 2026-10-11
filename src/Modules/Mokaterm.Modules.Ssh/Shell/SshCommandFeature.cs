using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.FileSystem;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// Commands over exec channels: one channel per command, on whatever client the session hands over. Stdin is closed
/// as soon as the command starts, so a command that reads it ends instead of waiting out the run's timeout.
/// </summary>
internal sealed class SshCommandFeature : IRemoteCommandFeature
{
	private readonly Func<CancellationToken, Task<SshClient>> _client;
	private readonly TimeProvider _time;

	/// <param name="client">The session's connected client; throws when the session has none any more.</param>
	public SshCommandFeature(Func<CancellationToken, Task<SshClient>> client, TimeProvider time)
	{
		_client = client;
		_time = time;
	}

	public Task<RemoteCommandOutcome> RunAsync(RemoteCommandRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (RemoteCommandRequest.Problem(request.Command) is { } problem)
		{
			throw new ArgumentException(problem, nameof(request));
		}

		string command = RemoteCommandRequest.Normalize(request.Command);
		return CommandExecution.RunAsync(token => StartAsync(command, token), request, _time, cancellationToken);
	}

	private async Task<IExecChannel> StartAsync(string command, CancellationToken cancellationToken)
	{
		SshClient client;
		try
		{
			client = await _client(cancellationToken);
		}
		catch (RemoteFileSystemException ex)
		{
			// A tab's session reports a closed connection the way its file browser needs it.
			throw new RemoteCommandException(ex.Message, ex);
		}

		RemoteCommand started = await RemoteCommand.StartAsync(client, command, input: null, cancellationToken);
		started.CloseInput();
		return started;
	}
}
