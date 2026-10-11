using Microsoft.AspNetCore.Components.Web;
using Moka.Red.ContextMenu;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// Which of a session's toolbar actions become buttons, in what order and with which lines between them, for one
/// layout and one hidden list. Kept apart from the component so each layout's rules can be tested as plain data.
/// </summary>
/// <param name="Slots">The buttons, left to right.</param>
/// <param name="MoreAlways">
/// The overflow menu's button shows at every width, because something only it reaches is hidden, or because the
/// layout is that menu. Otherwise it only appears once the toolbar is narrow enough to fold buttons into it.
/// </param>
internal sealed record ToolbarPlan(IReadOnlyList<ToolbarSlot> Slots, bool MoreAlways)
{
	/// <summary>Hides a button once the toolbar is under the first width step: the view and session groups go first.</summary>
	public const string FoldFirst = "mt-tb-fold-1";

	/// <summary>Hides a button under the second, narrower step: panels and tools follow. Connection buttons stay.</summary>
	public const string FoldSecond = "mt-tb-fold-2";

	private const int NeverFolds = int.MaxValue;

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
			return new ToolbarPlan([], MoreAlways: true);
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
		List<ToolbarAction> ordered = [.. shown.OrderBy(action => Rank(action, order))];
		List<ToolbarSlot> slots = new(ordered.Count);
		int widestEarlier = -1;
		ToolbarGroup? previous = null;
		foreach (ToolbarAction action in ordered)
		{
			int level = action.Pinned ? NeverFolds : FoldLevel(action.Group);
			bool divider = grouped && previous is { } last && last != action.Group;

			// A line goes once the group after it does, or once every group before it has, so the toolbar never starts
			// with one or shows two side by side.
			int dividerLevel = Math.Min(level, widestEarlier);
			slots.Add(new ToolbarSlot(action, divider, FoldClass(level), divider ? FoldClass(dividerLevel) : null));
			widestEarlier = Math.Max(widestEarlier, level);
			previous = action.Group;
		}

		return new ToolbarPlan(slots, anyHidden);
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

	private static int FoldLevel(ToolbarGroup group) => group switch
	{
		ToolbarGroup.View or ToolbarGroup.Session => 1,
		ToolbarGroup.Panels or ToolbarGroup.Tools => 2,
		_ => NeverFolds,
	};

	private static string? FoldClass(int level) => level switch
	{
		1 => FoldFirst,
		2 => FoldSecond,
		_ => null,
	};

	// A tool sits where the tools menu would, in the order the module registered it.
	private static int Rank(ToolbarAction action, string[] order)
	{
		string id = SessionToolbarButtons.IsTool(action.Id) ? SessionToolbarButtons.ToolsMenu : action.Id;
		int index = Array.IndexOf(order, id);
		return index < 0 ? order.Length : index;
	}
}
