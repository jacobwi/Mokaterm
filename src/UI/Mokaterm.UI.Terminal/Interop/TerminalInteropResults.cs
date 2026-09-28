namespace Mokaterm.UI.Terminal.Interop;

/// <summary>Grid and pixel size reported by terminal.js after a fit.</summary>
internal sealed record TerminalJsSize(int Cols, int Rows, int Width, int Height);

/// <summary>The id of a new xterm.js instance and its fitted size, or no size while the view is hidden.</summary>
internal sealed record TerminalCreateResult(int Id, TerminalJsSize? Size);

/// <summary>
/// One slice of the selection. <see cref="Next"/> is the offset to ask for next, or -1 after the last slice.
/// </summary>
internal sealed record SelectionChunk(string Text, int Next);

/// <summary>
/// Search outcome. <see cref="Index"/> is -1 when no match is active or there are too many to count. <see cref="Invalid"/>
/// is true when a regular expression search was given a pattern that does not parse.
/// </summary>
internal sealed record TerminalSearchResult(bool Found, int Index, int Count, bool Invalid = false);

/// <summary>How the find bar matches, in the shape the search addon takes.</summary>
internal sealed record TerminalSearchFlags(bool CaseSensitive, bool WholeWord, bool Regex);
