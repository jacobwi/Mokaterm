using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using Mokaterm.Abstractions.FileSystem;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Mokaterm.Modules.Ssh.FileSystem;

/// <summary>Maps SSH.NET failures during file operations, over SFTP or an exec channel, to <see cref="RemoteFileSystemException"/>.</summary>
internal static class RemoteFileErrors
{
	/// <summary>
	/// Maps failures that came from the SSH connection. Cancellation, <see cref="RemoteFileSystemException"/>s and anything
	/// else (a local stream error, a bug) return false so they propagate unchanged.
	/// </summary>
	public static bool TryMap(Exception exception, string? path, [NotNullWhen(true)] out RemoteFileSystemException? mapped)
	{
		ArgumentNullException.ThrowIfNull(exception);
		string target = path ?? "the file";
		mapped = exception switch
		{
			SftpPathNotFoundException => Create(RemoteFileErrorKind.NotFound, $"{target} does not exist."),
			SftpPermissionDeniedException => Create(RemoteFileErrorKind.PermissionDenied, $"Permission denied: {target}"),
			SftpException sftp => sftp.StatusCode switch
			{
				StatusCode.NoSuchFile => Create(RemoteFileErrorKind.NotFound, $"{target} does not exist."),
				StatusCode.PermissionDenied => Create(RemoteFileErrorKind.PermissionDenied, $"Permission denied: {target}"),
				StatusCode.OperationUnsupported => Create(RemoteFileErrorKind.NotSupported, "The server does not support this operation."),
				StatusCode.NoConnection or StatusCode.ConnectionLost => ConnectionLost(),
				_ => Create(RemoteFileErrorKind.Unknown, ServerMessage(sftp, target)),
			},
			SshConnectionException or SshOperationTimeoutException or SocketException or ObjectDisposedException => ConnectionLost(),
			NotSupportedException => Create(RemoteFileErrorKind.NotSupported, "The server does not support this operation."),
			SshException => Create(RemoteFileErrorKind.Unknown, exception.Message),
			_ => null,
		};

		return mapped is not null;

		RemoteFileSystemException ConnectionLost() => Create(RemoteFileErrorKind.ConnectionLost, "The connection to the server was lost.");

		RemoteFileSystemException Create(RemoteFileErrorKind kind, string message) => new(kind, message, path, exception);
	}

	public static RemoteFileSystemException NotFound(string path) =>
		new(RemoteFileErrorKind.NotFound, $"{path} does not exist.", path);

	public static RemoteFileSystemException AlreadyExists(string path) =>
		new(RemoteFileErrorKind.AlreadyExists, $"{path} already exists.", path);

	private static string ServerMessage(SftpException exception, string target) =>
		string.IsNullOrWhiteSpace(exception.Message) || exception.Message.Equals(exception.StatusCode.ToString(), StringComparison.OrdinalIgnoreCase)
			? $"The server could not complete the operation on {target}."
			: $"The server could not complete the operation on {target}: {exception.Message}";
}
