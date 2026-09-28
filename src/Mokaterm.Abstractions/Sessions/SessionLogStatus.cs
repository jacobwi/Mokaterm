namespace Mokaterm.Abstractions.Sessions;

/// <summary>What the recorder is doing for one session, for the toolbar button and its tooltip.</summary>
public sealed record SessionLogStatus
{
	public static readonly SessionLogStatus Off = new();

	public SessionLogState State { get; init; } = SessionLogState.Off;

	/// <summary>The file's name without its folder. Null until the first output arrives, which is when the file is created.</summary>
	public string? FileName { get; init; }

	/// <summary>How much output reached the file.</summary>
	public long Bytes { get; init; }

	/// <summary>Why it stopped, for a state of <see cref="SessionLogState.Full"/> or <see cref="SessionLogState.Failed"/>.</summary>
	public string? Message { get; init; }

	/// <summary>True while this session is being recorded, whether or not the file is still taking output.</summary>
	public bool IsOn => State != SessionLogState.Off;
}
