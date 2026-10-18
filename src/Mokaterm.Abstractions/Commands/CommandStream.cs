namespace Mokaterm.Abstractions.Commands;

/// <summary>Which of a command's two output streams a piece of output came from.</summary>
public enum CommandStream
{
	/// <summary>stdout.</summary>
	Output,

	/// <summary>stderr.</summary>
	Error,
}
