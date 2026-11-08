using Microsoft.AspNetCore.Components.Web;
using Moka.Red.ContextMenu;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// Which of a session's toolbar actions become buttons, in what order and with which lines between them, for one
/// layout and one hidden list. Kept apart from the component so each layout's rules can be tested as plain data.
/// </summary>
/// <param name="Runs">The buttons, left to right, in the groups that leave the bar together when it runs out of room.</param>
/// <param name="MoreAlways">
/// The overflow menu's button shows at every width, because something only it reaches is hidden, or because the
/// layout is that menu. Otherwise it only appears once the toolbar has run out of room for something.
/// </param>
internal sealed record ToolbarPlan(IReadOnlyList<ToolbarRun> Runs, bool MoreAlways)
{
	/// <summary>The overflow menu's button leaves last of all, since it is the way to everything that left before it.</summary>
	public const int MorePriority = 50;

	// The higher, the longer a group stays. Among equals Moka drops the last one first.
	private const int ViewAndSession = 10;
	private const int PanelsAndTools = 20;
	private const int Connection = 30;
	private const int Pinned = 40;

	// The grouped layout reads left to right by group; the flat one keeps the order the toolbar has always had.
	private static readonly string[] GroupedOrder =
	[
		SessionToolbarButtons.FileBrowser,
		SessionToolbarButtons.SavedCommands,
		SessionToolbarButtons.Find,
		SessionToolbarButtons.Theme,
		SessionToolbarButtons.Clear,
		SessionToolbarButtons.Record,
		SessionToolbarButtons.Broadcast,
		SessionToolbarButtons.Duplicate,
		SessionToolbarButtons.ToolsMenu,
		SessionToolbarButtons.Reconnect,
		SessionToolbarButtons.Disconnect,
		SessionToolbarButtons.Close,
	];

	private static readonly string[] FlatOrder =
	[
		SessionToolbarButtons.FileBrowser,
		SessionToolbarButtons.SavedCommands,
		SessionToolbarButtons.Broadcast,
		SessionToolbarButtons.Theme,
		SessionToolbarButtons.Find,
		SessionToolbarButtons.Clear,
		SessionToolbarButtons.Record,
		SessionToolbarButtons.ToolsMenu,
		SessionToolbarButtons.Reconnect,
		SessionToolbarButtons.Duplicate,
		SessionToolbarButtons.Disconnect,
		SessionToolbarButtons.Close,
	];

	/// <param name="actions">
	/// Everything the session can do now, each tool as its own action plus the tools menu when there are tools.
	/// </param>
	/// <param name="hidden">The ids the user turned off.</param>
	public static ToolbarPlan Build(SessionToolbarLayout layout, IReadOnlyList<ToolbarAction> actions, IReadOnlyCollection<string> hidden)
	{
		ArgumentNullException.ThrowIfNull(actions);
		ArgumentNullException.ThrowIfNull(hidden);
		if (layout == SessionToolbarLayout.Compact)
		{
			// Everything goes behind the menu except what is happening rather than what can be clicked: the
			// recording's dot and the pinned reconnect.
			return new ToolbarPlan(RunsOf(actions.Where(action => action.Pinned || action.Dot != ToolbarDot.None), dividers: false), MoreAlways: true);
		}

		bool grouped = layout == SessionToolbarLayout.Grouped;
		bool anyToolShown = actions.Any(action => SessionToolbarButtons.IsTool(action.Id) && !hidden.Contains(action.Id));
		List<ToolbarAction> shown = [];
		bool anyHidden = false;
		foreach (ToolbarAction action in actions)
		{
			bool isTool = SessionToolbarButtons.IsTool(action.Id);
			bool isMenu = action.Id == SessionToolbarButtons.ToolsMenu;

			// The grouped layout reaches tools through its menu; the flat one gives each its own button.
			if (grouped ? isTool : isMenu)
			{
				anyHidden |= grouped && isTool && hidden.Contains(action.Id);
				continue;
			}

			if (!action.Pinned && (hidden.Contains(action.Id) || (isMenu && !anyToolShown)))
			{
				anyHidden = true;
				continue;
			}

			shown.Add(action);
		}

		string[] order = grouped ? GroupedOrder : FlatOrder;
		return new ToolbarPlan(RunsOf(shown.OrderBy(action => Rank(action, order)), grouped), anyHidden);
	}

	/// <summary>
	/// The overflow menu: every action, each tool on its own line, grouped as the grouped layout groups them, with
	/// toggles checked and the theme as a submenu.
	/// </summary>
	public static IReadOnlyList<MokaContextMenuItem> BuildMenu(IReadOnlyList<ToolbarAction> actions)
	{
		ArgumentNullException.ThrowIfNull(actions);
		List<MokaContextMenuItem> items = [];
		ToolbarGroup? previous = null;
		foreach (ToolbarAction action in actions
			.Where(action => action.Id != SessionToolbarButtons.ToolsMenu)
			.OrderBy(action => action.Group)
			.ThenBy(action => Rank(action, GroupedOrder)))
		{
			ToolbarAction captured = action;
			items.Add(new MokaContextMenuItem
			{
				Text = action.MenuText ?? action.Title,
				Icon = action.Icon,
				Shortcut = action.Shortcut,
				Checked = action.Toggle && action.Active,
				DividerBefore = previous is { } last && last != action.Group,
				Children = action.Children?.Invoke(),
				OnClick = action.Children is null && action.Run is { } run ? () => run(new MouseEventArgs()) : null,
			});
			previous = action.Group;
		}

		return items;
	}

	private static List<ToolbarRun> RunsOf(IEnumerable<ToolbarAction> ordered, bool dividers)
	{
		List<ToolbarRun> runs = [];
		List<ToolbarAction> run = [];
		foreach (ToolbarAction action in ordered)
		{
			if (run.Count > 0 && run[^1].Group != action.Group)
			{
				runs.Add(Finish(run, dividers && runs.Count > 0));
				run = [];
			}

			run.Add(action);
		}

		if (run.Count > 0)
		{
			runs.Add(Finish(run, dividers && runs.Count > 0));
		}

		return runs;
	}

	private static ToolbarRun Finish(List<ToolbarAction> run, bool divider) => new(run, divider, run.Max(PriorityOf));

	private static int PriorityOf(ToolbarAction action) => action.Pinned ? Pinned : action.Group switch
	{
		ToolbarGroup.View or ToolbarGroup.Session => ViewAndSession,
		ToolbarGroup.Panels or ToolbarGroup.Tools => PanelsAndTools,
		_ => Connection,
	};

	// A tool sits where the tools menu would, in the order the module registered it.
	private static int Rank(ToolbarAction action, string[] order)
	{
		string id = SessionToolbarButtons.IsTool(action.Id) ? SessionToolbarButtons.ToolsMenu : action.Id;
		int index = Array.IndexOf(order, id);
		return index < 0 ? order.Length : index;
	}
}
