using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Security;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// Commands over exec channels: one channel per command, on whatever client the session hands over. A request's
/// <see cref="RemoteCommandRequest.Input"/> is written as one line once the command started; without one, stdin is
/// closed as soon as the command starts, so a command that reads it ends instead of waiting out the run's timeout.
/// </summary>
internal sealed class SshCommandFeature : IRemoteCommandFeature
{
	private readonly Func<string, SecretBuffer?, bool, CancellationToken, Task<IExecChannel>> _start;
	private readonly TimeProvider _time;

	/// <param name="client">The session's connected client; throws when the session has none any more.</param>
	public SshCommandFeature(Func<CancellationToken, Task<SshClient>> client, TimeProvider time)
		: this((command, input, runsLong, token) => StartOnAsync(client, command, input, runsLong, token), time)
	{
	}

	/// <param name="start">
	/// Starts a command line, writes the input it is given on stdin as one line and closes stdin, also when there is no
	/// input; the bool is the channel's <c>runsLong</c>. Tests run the commands in a local shell through this.
	/// </param>
	internal SshCommandFeature(Func<string, SecretBuffer?, bool, CancellationToken, Task<IExecChannel>> start, TimeProvider time)
	{
		_start = start;
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
		return CommandExecution.RunAsync(token => _start(command, request.Input, request.RunsLong, token), request, _time, cancellationToken);
	}

	private static async Task<IExecChannel> StartOnAsync(
		Func<CancellationToken, Task<SshClient>> clientSource,
		string command,
		SecretBuffer? input,
		bool runsLong,
		CancellationToken cancellationToken)
	{
		SshClient client;
		try
		{
			client = await clientSource(cancellationToken);
		}
		catch (RemoteFileSystemException ex)
		{
			// A tab's session reports a closed connection the way its file browser needs it.
			throw new RemoteCommandException(ex.Message, ex);
		}

		RemoteCommand started = await RemoteCommand.StartAsync(client, command, input, runsLong, cancellationToken);
		if (input is null)
		{
			// Nothing was written, and SSH.NET sends EOF only after data, so stdin has to be closed by hand.
			started.CloseInput();
		}

		return started;
	}
}
