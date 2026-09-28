using System.Net.Sockets;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Abstractions.Diagnostics;

/// <summary>
/// What a log line may say about a failure. Exception messages here are written for the user and carry host names,
/// account names and remote paths, none of which may reach a plain log file; the types and kinds are enough to tell one
/// failure from another. Log the exception itself only where its message cannot hold any of those.
/// </summary>
public static class LogSafe
{
	/// <summary>
	/// The exception's type with its kind or error code, then the same for the exception at its root, as in
	/// <c>RemoteFileSystemException (PermissionDenied) from SftpPermissionDeniedException</c>.
	/// </summary>
	public static string Describe(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		Exception root = exception.GetBaseException();
		return ReferenceEquals(root, exception) ? Name(exception) : Name(exception) + " from " + Name(root);
	}

	private static string Name(Exception exception) => exception switch
	{
		RemoteFileSystemException remote => nameof(RemoteFileSystemException) + " (" + remote.Kind.ToString() + ")",
		ProtocolConnectException connect => nameof(ProtocolConnectException) + " (" + connect.Failure.ToString() + ")",
		SocketException socket => nameof(SocketException) + " (" + socket.SocketErrorCode.ToString() + ")",
		_ => exception.GetType().Name,
	};
}
