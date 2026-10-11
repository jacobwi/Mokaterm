using Moka.Red.ContextMenu;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The session toolbar's three layouts, the hidden list and the folding classes, as plain data: what becomes a button,
/// in which order, where the lines go, and that nothing ever becomes unreachable.
/// </summary>
public sealed class ToolbarPlanTests
{
	private const string Docker = "tool.docker";
	private const string Tunnels = "tool.port-forwarding";

	[Fact]
	public void Grouped_ReadsPanelsViewSessionToolsThenConnection_WithALineBetweenGroups()
	{
		ToolbarPlan plan = ToolbarPlan.Build(SessionToolbarLayout.Grouped, Everything(), []);

		Assert.Equal(
			["files", "commands", "|", "find", "theme", "clear", "|", "record", "broadcast", "duplicate", "|", "tools", "|", "disconnect", "close"],
			Row(plan));
		Assert.False(plan.MoreAlways);
	}

	[Fact]
	public void Grouped_ReachesToolsThroughItsMenu_NotAButtonEach()
	{
		ToolbarPlan plan = ToolbarPlan.Build(SessionToolbarLayout.Grouped, Everything(), []);

		Assert.DoesNotContain(plan.Slots, slot => SessionToolbarButtons.IsTool(slot.Action.Id));
		Assert.Contains(plan.Slots, slot => slot.Action.Id == SessionToolbarButtons.ToolsMenu);
	}

	[Fact]
	public void Flat_KeepsTheOldOrder_EachToolWithItsOwnButton_AndNoLines()
	{
		ToolbarPlan plan = ToolbarPlan.Build(SessionToolbarLayout.Flat, Everything(), []);

		Assert.Equal(
			["files", "commands", "broadcast", "theme", "find", "clear", "record", Tunnels, Docker, "duplicate", "disconnect", "close"],
			Row(plan));
	}

	[Fact]
	public void Compact_ShowsNoButtons_OnlyTheMenuThatHoldsEverything()
	{
		ToolbarPlan plan = ToolbarPlan.Build(SessionToolbarLayout.Compact, Everything(), []);

		Assert.Empty(plan.Slots);
		Assert.True(plan.MoreAlways);
	}

	[Fact]
	public void HiddenButtons_LeaveTheBar_AndKeepTheOverflowMenuShowing()
	{
		ToolbarPlan plan = ToolbarPlan.Build(SessionToolbarLayout.Grouped, Everything(), ["find", "clear", "theme", "duplicate"]);

		Assert.Equal(["files", "commands", "|", "record", "broadcast", "|", "tools", "|", "disconnect", "close"], Row(plan));
		Assert.True(plan.MoreAlways);
	}

	[Fact]
	public void Grouped_EveryToolHidden_DropsTheToolsMenu()
	{
		ToolbarPlan plan = ToolbarPlan.Build(SessionToolbarLayout.Grouped, Everything(), [Docker, Tunnels]);

		Assert.DoesNotContain(plan.Slots, slot => slot.Action.Id == SessionToolbarButtons.ToolsMenu);
		Assert.True(plan.MoreAlways);
	}

	[Fact]
	public void Grouped_OneToolHidden_KeepsTheMenuButSaysTheOverflowHasSomething()
	{
		ToolbarPlan plan = ToolbarPlan.Build(SessionToolbarLayout.Grouped, Everything(), [Docker]);

		Assert.Contains(plan.Slots, slot => slot.Action.Id == SessionToolbarButtons.ToolsMenu);
		Assert.True(plan.MoreAlways);
	}

	[Fact]
	public void Flat_HidingTheToolsMenu_ChangesNothing_SinceFlatHasNone()
	{
		ToolbarPlan plan = ToolbarPlan.Build(SessionToolbarLayout.Flat, Everything(), [SessionToolbarButtons.ToolsMenu]);

		Assert.False(plan.MoreAlways);
		Assert.Contains(plan.Slots, slot => slot.Action.Id == Docker);
	}

	[Fact]
	public void Reconnect_IsPinned_HiddenOrNot()
	{
		List<ToolbarAction> actions = [.. Everything(), Action(SessionToolbarButtons.Reconnect, ToolbarGroup.Connection) with { Pinned = true }];

		ToolbarPlan plan = ToolbarPlan.Build(SessionToolbarLayout.Grouped, actions, [SessionToolbarButtons.Reconnect]);

		ToolbarSlot reconnect = Assert.Single(plan.Slots, slot => slot.Action.Id == SessionToolbarButtons.Reconnect);
		Assert.Null(reconnect.FoldClass);
	}

	[Fact]
	public void Folding_ViewAndSessionGoFirst_PanelsAndToolsNext_ConnectionNever()
	{
		ToolbarPlan plan = ToolbarPlan.Build(SessionToolbarLayout.Grouped, Everything(), []);

		Assert.Equal(ToolbarPlan.FoldFirst, Slot(plan, "find").FoldClass);
		Assert.Equal(ToolbarPlan.FoldFirst, Slot(plan, "record").FoldClass);
		Assert.Equal(ToolbarPlan.FoldSecond, Slot(plan, "files").FoldClass);
		Assert.Equal(ToolbarPlan.FoldSecond, Slot(plan, "tools").FoldClass);
		Assert.Null(Slot(plan, "close").FoldClass);
	}

	// A line must never be left at the start of the bar or next to another one once groups fold away.
	[Fact]
	public void Folding_TheLineBeforeConnection_GoesOnceEverythingBeforeItHas()
	{
		ToolbarPlan plan = ToolbarPlan.Build(SessionToolbarLayout.Grouped, Everything(), []);

		Assert.Equal(ToolbarPlan.FoldFirst, Slot(plan, "find").DividerFoldClass);
		Assert.Equal(ToolbarPlan.FoldSecond, Slot(plan, "tools").DividerFoldClass);
		Assert.Equal(ToolbarPlan.FoldSecond, Slot(plan, "disconnect").DividerFoldClass);
	}

	[Fact]
	public void Menu_HoldsEveryActionAndEveryTool_GroupedWithLines_TogglesChecked()
	{
		List<ToolbarAction> actions = [.. Everything()];
		actions[actions.FindIndex(action => action.Id == "record")] = Action("record", ToolbarGroup.Session) with { Toggle = true, Active = true };

		IReadOnlyList<MokaContextMenuItem> menu = ToolbarPlan.BuildMenu(actions);

		Assert.Equal(12, menu.Count);
		Assert.DoesNotContain(menu, item => item.Text == SessionToolbarButtons.ToolsMenu);
		Assert.Contains(menu, item => item.Text == Docker);
		Assert.True(menu.Single(item => item.Text == "record").Checked);
		Assert.Equal(4, menu.Count(item => item.DividerBefore));
	}

	[Fact]
	public void Menu_ShowsAnActionsOwnMenuAsASubmenu()
	{
		List<ToolbarAction> actions =
		[
			Action("theme", ToolbarGroup.View) with { Children = () => [new MokaContextMenuItem { Text = "Dracula" }] },
		];

		MokaContextMenuItem theme = Assert.Single(ToolbarPlan.BuildMenu(actions));

		Assert.True(theme.HasChildren);
		Assert.Null(theme.OnClick);
	}

	private static List<ToolbarAction> Everything() =>
	[
		Action("files", ToolbarGroup.Panels),
		Action("commands", ToolbarGroup.Panels),
		Action("find", ToolbarGroup.View),
		Action("theme", ToolbarGroup.View),
		Action("clear", ToolbarGroup.View),
		Action("record", ToolbarGroup.Session),
		Action("broadcast", ToolbarGroup.Session),
		Action("duplicate", ToolbarGroup.Session),
		Action(Tunnels, ToolbarGroup.Tools),
		Action(Docker, ToolbarGroup.Tools),
		Action(SessionToolbarButtons.ToolsMenu, ToolbarGroup.Tools),
		Action("disconnect", ToolbarGroup.Connection),
		Action("close", ToolbarGroup.Connection),
	];

	private static ToolbarAction Action(string id, ToolbarGroup group) => new()
	{
		Id = id,
		Title = id,
		Icon = MokaIcons.Action.Search,
		Group = group,
		Run = _ => Task.CompletedTask,
	};

	private static ToolbarSlot Slot(ToolbarPlan plan, string id) => plan.Slots.Single(slot => slot.Action.Id == id);

	private static List<string> Row(ToolbarPlan plan)
	{
		List<string> row = [];
		foreach (ToolbarSlot slot in plan.Slots)
		{
			if (slot.Divider)
			{
				row.Add("|");
			}

			row.Add(slot.Action.Id);
		}

		return row;
	}
}
