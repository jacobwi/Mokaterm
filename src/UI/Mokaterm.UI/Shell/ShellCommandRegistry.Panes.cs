using System.Globalization;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Shell;

/// <summary>The pane commands: focusing a pane, moving the active tab between panes, and the named layouts.</summary>
internal sealed partial class ShellCommandRegistry
{
	private const string PanesGroup = "Panes";

	private const string AloneInPane = "This tab is the only one in its pane, so there is nothing to split off.";

	private List<CommandDefinition> BuildPaneDefinitions()
	{
		List<CommandDefinition> definitions = [];
		for (int position = 0; position < SessionWorkspace.MaxPanes; position++)
		{
			int pane = position;
			string number = (pane + 1).ToString(CultureInfo.InvariantCulture);
			definitions.Add(new(
				ShellCommandIds.FocusPane[pane],
				$"Focus pane {number}",
				PanesGroup,
				MokatermIcons.SplitView,
				Sync(() => FocusPane(pane)),
				$"Point the commands and the keyboard at pane {number}",
				"split switch go"));
		}

		for (int position = 0; position < SessionWorkspace.MaxPanes; position++)
		{
			int pane = position;
			string number = (pane + 1).ToString(CultureInfo.InvariantCulture);
			definitions.Add(new(
				ShellCommandIds.MoveToPane[pane],
				$"Move tab to pane {number}",
				PanesGroup,
				MokatermIcons.SplitView,
				() => MoveToPane(pane),
				pane == 0 ? "Move the active tab to the first pane" : $"Move the active tab to pane {number}, opening it when it is the next one",
				"split send"));
		}

		definitions.AddRange(
		[
			new(ShellCommandIds.NewPaneRight, "Move tab to a new pane on the right", PanesGroup, MokatermIcons.SplitView, () => MoveToNewPane(PaneDirection.Right), "Split the active tab's pane and put the tab on the right", "split side by side vertical"),
			new(ShellCommandIds.NewPaneDown, "Move tab to a new pane below", PanesGroup, MokatermIcons.SplitStacked, () => MoveToNewPane(PaneDirection.Down), "Split the active tab's pane and put the tab underneath", "split stacked horizontal"),
			new(ShellCommandIds.NewPaneLeft, "Move tab to a new pane on the left", PanesGroup, MokatermIcons.SplitView, () => MoveToNewPane(PaneDirection.Left), "Split the active tab's pane and put the tab on the left", "split side by side vertical"),
			new(ShellCommandIds.NewPaneUp, "Move tab to a new pane above", PanesGroup, MokatermIcons.SplitStacked, () => MoveToNewPane(PaneDirection.Up), "Split the active tab's pane and put the tab on top", "split stacked horizontal"),
			new(ShellCommandIds.SinglePane, "Put every tab in one pane", PanesGroup, MokatermIcons.SinglePane, () => Arrange(PaneArrangement.Single), "Close the split and keep every tab", "unsplit merge join single layout"),
			new(ShellCommandIds.LayoutSideBySide, "Layout: two side by side", PanesGroup, MokatermIcons.SplitView, () => Arrange(PaneArrangement.SideBySide), "Spread the tabs over two panes, left and right", "split columns"),
			new(ShellCommandIds.LayoutStacked, "Layout: two stacked", PanesGroup, MokatermIcons.SplitStacked, () => Arrange(PaneArrangement.Stacked), "Spread the tabs over two panes, top and bottom", "split rows"),
			new(ShellCommandIds.LayoutThree, "Layout: one large and two stacked", PanesGroup, MokatermIcons.SplitThree, () => Arrange(PaneArrangement.MainAndStack), "Spread the tabs over a large pane and two stacked beside it", "split three main"),
			new(ShellCommandIds.LayoutGrid, "Layout: four in a grid", PanesGroup, MokatermIcons.SplitGrid, () => Arrange(PaneArrangement.Grid), "Spread the tabs over four panes, two over two", "split quad tiles"),
		]);

		return definitions;
	}

	private void FocusPane(int position)
	{
		_shell.CloseSettings();
		if (_workspace.FocusPaneAt(position) is { } sessionId)
		{
			// The commands follow the focused pane, and the keys have to land there too.
			_commands.RequestTerminalFocus(sessionId);
		}
	}

	private Task MoveToPane(int position) => WithActiveSession(session =>
	{
		_shell.CloseSettings();
		int panes = _workspace.PaneCount;
		if (!_workspace.MoveToPane(session.Id, position) && position >= panes)
		{
			_interaction.Notify(NoticeSeverity.Info, AloneInPane);
		}

		return Task.CompletedTask;
	});

	private Task MoveToNewPane(PaneDirection direction) => WithActiveSession(session =>
	{
		_shell.CloseSettings();
		if (!_workspace.MoveToNewPane(session.Id, direction))
		{
			_interaction.Notify(
				NoticeSeverity.Info,
				_workspace.PaneCount >= SessionWorkspace.MaxPanes
					? string.Create(CultureInfo.CurrentCulture, $"The workspace holds {SessionWorkspace.MaxPanes} panes at most.")
					: AloneInPane);
		}

		return Task.CompletedTask;
	});

	private Task Arrange(PaneArrangement arrangement) => WithActiveSession(_ =>
	{
		_shell.CloseSettings();
		int tabs = _workspace.Tabs.Count;
		if (_workspace.Arrange(arrangement) < PaneLayout.PaneCount(arrangement))
		{
			_interaction.Notify(
				NoticeSeverity.Info,
				string.Create(CultureInfo.CurrentCulture, $"Each pane needs a tab of its own, and {tabs} {(tabs == 1 ? "tab is" : "tabs are")} open."));
		}

		return Task.CompletedTask;
	});
}
