using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>Short, user-facing messages for file system failures.</summary>
internal static class RemoteErrorText
{
	public static string Describe(Exception exception) => exception switch
	{
		RemoteFileSystemException remote => Describe(remote),
		NotSupportedException => "Not supported for this connection.",
		OperationCanceledException => "Cancelled.",
		_ => Fallback(exception.Message, "Something went wrong."),
	};

	public static bool IsPermissionDenied(Exception exception) =>
		exception is RemoteFileSystemException { Kind: RemoteFileErrorKind.PermissionDenied };

	private static string Describe(RemoteFileSystemException exception) => exception.Kind switch
	{
		RemoteFileErrorKind.NotFound => "No such file or folder.",
		RemoteFileErrorKind.PermissionDenied => "Permission denied.",
		RemoteFileErrorKind.AlreadyExists => "Something with that name already exists.",
		RemoteFileErrorKind.DirectoryNotEmpty => "The folder is not empty.",
		// Every protocol words these as sentences for the user ("The server has no unzip command, ..."), and the general
		// answer would hide which tool or command is missing.
		RemoteFileErrorKind.NotSupported => Fallback(exception.Message, "The server does not support this."),
		RemoteFileErrorKind.ConnectionLost => "The connection was lost.",
		RemoteFileErrorKind.ElevationFailed => Fallback(exception.Message, "Could not switch to root."),
		_ => Fallback(exception.Message, "The server reported an error."),
	};

	private static string Fallback(string? message, string fallback) =>
		string.IsNullOrWhiteSpace(message) ? fallback : message;
}
