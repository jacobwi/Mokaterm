using System.Net.Sockets;
using System.Security.Authentication;
using FluentFTP;
using FluentFTP.Exceptions;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Ftp.FileSystem;

/// <summary>Turns FTP replies and FluentFTP exceptions into <see cref="RemoteFileErrorKind"/> and user-facing messages.</summary>
internal static class FtpErrors
{
	private static readonly string[] NotEmptyHints = ["not empty"];

	private static readonly string[] NotFoundHints =
		["no such file", "no such directory", "not found", "not exist", "doesn't exist", "does not exist", "cannot find", "can't find", "could not find", "nonexistent", "non-existent"];

	private static readonly string[] AlreadyExistsHints = ["already exist", "file exists", "directory exists", "folder exists"];

	private static readonly string[] PermissionHints =
		["permission", "denied", "not allowed", "forbidden", "not authorized", "unauthorized", "read-only", "read only", "insufficient privilege"];

	private static readonly string[] ConnectionLimitHints = ["too many", "maximum", "limit"];

	private static readonly string[] TlsHints = ["tls", "ssl", "encrypt"];

	/// <summary>
	/// Classifies a reply. <paramref name="unavailable"/> is the kind to assume for a bare 550 ("file unavailable") whose text
	/// gives no hint, which depends on the command: missing for a listing, refused for a new folder.
	/// </summary>
	public static RemoteFileErrorKind ClassifyReply(string? code, string? message, RemoteFileErrorKind unavailable)
	{
		RemoteFileErrorKind? hinted = ClassifyMessage(message);
		return code switch
		{
			"421" or "425" or "426" => RemoteFileErrorKind.ConnectionLost,
			"500" or "502" or "504" => RemoteFileErrorKind.NotSupported,
			"530" or "532" or "534" => RemoteFileErrorKind.PermissionDenied,
			"521" => RemoteFileErrorKind.AlreadyExists,
			"553" => hinted is RemoteFileErrorKind.AlreadyExists ? RemoteFileErrorKind.AlreadyExists : RemoteFileErrorKind.PermissionDenied,
			"550" => hinted ?? unavailable,
			_ => hinted ?? RemoteFileErrorKind.Unknown,
		};
	}

	/// <summary>Reads the kind from the reply text, which servers word differently for the same code.</summary>
	public static RemoteFileErrorKind? ClassifyMessage(string? message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return null;
		}

		// Order matters: "does not exist" must win over "exist", and "directory not empty" over "denied" style wording.
		if (ContainsAny(message, NotEmptyHints))
		{
			return RemoteFileErrorKind.DirectoryNotEmpty;
		}

		if (ContainsAny(message, NotFoundHints))
		{
			return RemoteFileErrorKind.NotFound;
		}

		if (ContainsAny(message, AlreadyExistsHints))
		{
			return RemoteFileErrorKind.AlreadyExists;
		}

		return ContainsAny(message, PermissionHints) ? RemoteFileErrorKind.PermissionDenied : null;
	}

	/// <summary>500, 502 and 504: the server does not know or does not implement the command.</summary>
	public static bool IsNotImplemented(string? code) => code is "500" or "502" or "504";

	/// <summary>The server's own reply text. FluentFTP's <see cref="Exception.Message"/> puts "Code: 550 Message: " in front of it.</summary>
	public static string ReplyText(FtpCommandException exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		string message = exception.Message ?? "";
		string prefix = $"Code: {exception.CompletionCode} Message: ";
		return (message.StartsWith(prefix, StringComparison.Ordinal) ? message[prefix.Length..] : message).Trim();
	}

	/// <summary>The server refuses another connection from this client, which is no reason to ask for a new password.</summary>
	public static bool IsConnectionLimit(FtpCommandException exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		return exception.CompletionCode == "421"
			|| exception.CompletionCode is "530" or "550" && ContainsAny(ReplyText(exception), ConnectionLimitHints);
	}

	/// <summary>The server only accepts logins over TLS.</summary>
	public static bool IsTlsRequired(FtpCommandException exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		return exception.CompletionCode is "530" or "534" or "550" && ContainsAny(ReplyText(exception), TlsHints);
	}

	/// <summary>The server rejected the user name or password, as opposed to refusing the connection for another reason.</summary>
	public static bool IsLoginRejected(FtpCommandException exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		return exception is FtpAuthenticationException
			&& exception.CompletionCode is "530" or "430"
			&& !IsConnectionLimit(exception)
			&& !IsTlsRequired(exception);
	}

	/// <summary>
	/// True when a command got no answer at all, so the connection broke or was already gone. FluentFTP reports that in
	/// many shapes, including a NullReferenceException from a listing on a closed connection, so this lists what an
	/// answer looks like instead.
	/// </summary>
	public static bool IsNoAnswer(Exception exception)
	{
		for (Exception? current = exception; current is not null; current = current.InnerException)
		{
			if (current is FtpCommandException or RemoteFileSystemException or FtpSanitizeException or FtpListParseException or OperationCanceledException)
			{
				return false;
			}
		}

		return true;
	}

	public static string DescribeReply(FtpCommandException exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		return DescribeReply(exception.CompletionCode, ReplyText(exception));
	}

	/// <summary>"550 No such file or directory", or just the code when the server sent no text.</summary>
	public static string DescribeReply(string? code, string? message)
	{
		string text = message?.Trim() ?? "";
		if (string.IsNullOrEmpty(code))
		{
			return text;
		}

		return text.Length == 0 ? code : $"{code} {text}";
	}

	/// <summary>Maps any failure of an operation on <paramref name="path"/> to a <see cref="RemoteFileSystemException"/>.</summary>
	/// <param name="action">A verb phrase for the message, such as "list" or "delete".</param>
	public static RemoteFileSystemException ToRemoteException(Exception exception, string action, string path, RemoteFileErrorKind unavailable)
	{
		ArgumentNullException.ThrowIfNull(exception);
		for (Exception? current = exception; current is not null; current = current.InnerException)
		{
			switch (current)
			{
				case RemoteFileSystemException remote:
					return remote;
				case FtpCommandException command:
					return Create(ClassifyReply(command.CompletionCode, ReplyText(command), unavailable), action, path, DescribeReply(command), exception);
				case FtpSanitizeException:
					return Create(RemoteFileErrorKind.Unknown, action, path, "the path contains characters that cannot be sent over FTP", exception);
				case FtpMissingObjectException:
					return Create(RemoteFileErrorKind.NotFound, action, path, "it does not exist", exception);
				case ProtocolConnectException connect:
					return Create(
						connect.Failure is ConnectFailure.AuthenticationFailed or ConnectFailure.HostIdentityRejected ? RemoteFileErrorKind.PermissionDenied : RemoteFileErrorKind.ConnectionLost,
						action,
						path,
						connect.Message,
						exception);
				case TimeoutException:
					return Create(RemoteFileErrorKind.ConnectionLost, action, path, "the server did not respond in time", exception);
				case SocketException or AuthenticationException or FtpMissingSocketException or ObjectDisposedException or IOException:
				case NullReferenceException when IsFromFluentFtp(current):
					return Create(RemoteFileErrorKind.ConnectionLost, action, path, "the connection to the server was lost", exception);
				default:
					break;
			}
		}

		return Create(RemoteFileErrorKind.Unknown, action, path, exception.Message, exception);
	}

	public static RemoteFileSystemException Create(RemoteFileErrorKind kind, string action, string path, string reason, Exception? innerException = null)
	{
		string trimmed = reason.Trim().TrimEnd('.');
		return new RemoteFileSystemException(kind, $"Could not {action} {path}: {trimmed}.", path, innerException);
	}

	// FluentFTP dereferences its already closed control stream in some paths, listings among them, when the server
	// dropped the connection.
	private static bool IsFromFluentFtp(Exception exception) =>
		exception.TargetSite?.DeclaringType?.Assembly == typeof(FtpListItem).Assembly;

	private static bool ContainsAny(string? text, string[] hints)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}

		foreach (string hint in hints)
		{
			if (text.Contains(hint, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
