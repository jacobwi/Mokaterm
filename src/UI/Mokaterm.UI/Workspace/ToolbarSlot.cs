namespace Mokaterm.UI.Workspace;

/// <summary>A button as the toolbar draws it.</summary>
/// <param name="Divider">Draws a line before the button, which starts a group.</param>
/// <param name="FoldClass">The class that hides the button once the toolbar is narrow, or null when it never folds.</param>
/// <param name="DividerFoldClass">The same for the line before it.</param>
internal sealed record ToolbarSlot(ToolbarAction Action, bool Divider, string? FoldClass, string? DividerFoldClass);
