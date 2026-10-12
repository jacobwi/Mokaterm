using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.Common.Editing;

/// <summary>What <see cref="IRemoteTextEditor.EditAsync"/> came to.</summary>
public sealed class RemoteTextEditResult
{
	private RemoteTextEditResult(RemoteTextEditOutcome outcome, TextFileRefusal refusal, string? message, Exception? error)
	{
		Outcome = outcome;
		Refusal = refusal;
		Message = message;
		Error = error;
	}

	public static RemoteTextEditResult Closed { get; } = new(RemoteTextEditOutcome.Closed, TextFileRefusal.None, null, null);

	public static RemoteTextEditResult Saved { get; } = new(RemoteTextEditOutcome.Saved, TextFileRefusal.None, null, null);

	public RemoteTextEditOutcome Outcome { get; }

	/// <summary>For <see cref="RemoteTextEditOutcome.Refused"/>, why the file was not opened.</summary>
	public TextFileRefusal Refusal { get; }

	/// <summary>For <see cref="RemoteTextEditOutcome.Failed"/>, the sentence to show.</summary>
	public string? Message { get; }

	/// <summary>
	/// For <see cref="RemoteTextEditOutcome.Failed"/>, what failed, so a caller can offer more than the sentence:
	/// another try as root after <see cref="IsPermissionDenied"/>, for one.
	/// </summary>
	public Exception? Error { get; }

	/// <summary>True when the file system refused the login access to the file or its folder.</summary>
	public bool IsPermissionDenied => Error is RemoteFileSystemException { Kind: RemoteFileErrorKind.PermissionDenied };

	public static RemoteTextEditResult Refused(TextFileRefusal refusal) => new(RemoteTextEditOutcome.Refused, refusal, null, null);

	public static RemoteTextEditResult Failed(Exception error, string message)
	{
		ArgumentNullException.ThrowIfNull(error);
		ArgumentException.ThrowIfNullOrWhiteSpace(message);
		return new RemoteTextEditResult(RemoteTextEditOutcome.Failed, TextFileRefusal.None, message, error);
	}
}
