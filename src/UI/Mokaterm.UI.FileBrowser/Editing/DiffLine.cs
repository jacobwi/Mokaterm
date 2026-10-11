namespace Mokaterm.UI.FileBrowser.Editing;

/// <summary>
/// One row of a line diff. Numbers are one-based and zero on the side a line is not on: an added line has no old
/// number and a removed one no new number. A skipped row stands for <see cref="Count"/> unchanged lines that start at
/// its two numbers, and carries no text.
/// </summary>
internal readonly record struct DiffLine(DiffLineKind Kind, int OldNumber, int NewNumber, string Text, int Count = 1);
