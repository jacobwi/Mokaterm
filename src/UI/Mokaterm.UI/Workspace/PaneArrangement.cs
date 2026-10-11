namespace Mokaterm.UI.Workspace;

/// <summary>The layouts the commands offer. Directional splits can build others; these are the ones with a name.</summary>
internal enum PaneArrangement
{
	/// <summary>Every tab in one pane.</summary>
	Single,

	/// <summary>Two panes, left and right.</summary>
	SideBySide,

	/// <summary>Two panes, top and bottom.</summary>
	Stacked,

	/// <summary>One large pane on the left and two stacked on its right.</summary>
	MainAndStack,

	/// <summary>Four panes, two over two.</summary>
	Grid,
}
