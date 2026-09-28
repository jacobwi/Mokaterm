namespace Mokaterm.Abstractions.Terminal;

/// <summary>
/// Outcome of <see cref="ITerminalThemeConverter.Import"/>: a theme and the format it was read from, or a message
/// saying why nothing could be read.
/// </summary>
public sealed record TerminalThemeImport
{
	public TerminalTheme? Theme { get; init; }

	public TerminalThemeFormat? Format { get; init; }

	/// <summary>Why the text could not be read, for the user. Null when <see cref="Theme"/> is set.</summary>
	public string? Error { get; init; }

	public bool Succeeded => Theme is not null;

	public static TerminalThemeImport Success(TerminalTheme theme, TerminalThemeFormat format) => new() { Theme = theme, Format = format };

	public static TerminalThemeImport Failure(string error) => new() { Error = error };
}
