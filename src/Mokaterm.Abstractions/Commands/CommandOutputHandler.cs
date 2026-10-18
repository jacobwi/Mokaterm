namespace Mokaterm.Abstractions.Commands;

/// <summary>
/// Hears a command's output while it runs, one piece at a time as it is read, for a view that shows a long command
/// (a build, a push) as it goes. It is called on the loop that reads the stream, and stdout and stderr are read side by
/// side, so it can be called from two threads at once; it has to return at once and must not throw. The bytes are only
/// valid during the call: the reader reuses its buffer for the next piece.
/// </summary>
/// <remarks>
/// Nothing waits for the handler to catch up and nothing is queued for it, so a handler that keeps what it hears has to
/// bound that itself. A piece can end in the middle of a line or of a UTF-8 character.
/// </remarks>
public delegate void CommandOutputHandler(CommandStream stream, ReadOnlySpan<byte> bytes);
