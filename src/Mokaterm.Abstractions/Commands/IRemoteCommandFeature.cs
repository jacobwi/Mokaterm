namespace Mokaterm.Abstractions.Commands;

/// <summary>
/// Session feature for protocols that run a command to completion without a terminal, which for SSH is an exec channel.
/// Reached through <see cref="Protocols.IProtocolSession.GetFeature{TFeature}"/> like the file system, and offered only by
/// protocols that declare <see cref="Protocols.ProtocolCapabilities.Commands"/>.
/// </summary>
/// <remarks>
/// Nothing a command says is logged, here or by a caller: a command line can carry a token, and its output can be
/// anything at all.
/// </remarks>
public interface IRemoteCommandFeature
{
	/// <summary>
	/// Runs <paramref name="request"/> and waits for it to end. Output past <see cref="RemoteCommandRequest.OutputLimitBytes"/>
	/// on either stream is read and dropped rather than kept, so a chatty command still reports its exit status. Cancelling
	/// asks the server to stop the command and closes its channel.
	/// </summary>
	/// <exception cref="RemoteCommandException">
	/// The command could not start, the connection dropped while it ran, or a stream went past what the protocol is willing
	/// to read for one command (64 MB for SSH), in which case the command is stopped.
	/// </exception>
	Task<RemoteCommandOutcome> RunAsync(RemoteCommandRequest request, CancellationToken cancellationToken);
}
