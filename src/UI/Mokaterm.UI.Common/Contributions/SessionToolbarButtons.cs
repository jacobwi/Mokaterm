using Moka.Red.Icons;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.UI.Common.Contributions;

/// <summary>
/// The buttons of the session toolbar by id. The shell draws them and the appearance page lists them for hiding, and
/// the two live in libraries that do not know each other, so the names they share are kept here.
/// </summary>
public static class SessionToolbarButtons
{
	public const string FileBrowser = "files";

	public const string SavedCommands = "commands";

	public const string Find = "find";

	public const string Theme = "theme";

	public const string Clear = "clear";

	public const string Record = "record";

	public const string Broadcast = "broadcast";

	public const string Duplicate = "duplicate";

	/// <summary>The grouped layout's menu of every tool.</summary>
	public const string ToolsMenu = "tools";

	public const string Disconnect = "disconnect";

	public const string Close = "close";

	/// <summary>Only there while the session is down, and the thing to do next then, so it cannot be hidden.</summary>
	public const string Reconnect = "reconnect";

	private const string ToolPrefix = "tool.";

	/// <summary>The shell's own buttons that can be hidden, in the order the settings page lists them.</summary>
	public static IReadOnlyList<SessionToolbarButton> BuiltIn { get; } =
	[
		new(FileBrowser, "File browser", MokatermIcons.SplitView),
		new(SavedCommands, "Saved commands", MokatermIcons.Command),
		new(Find, "Find", MokaIcons.Action.Search),
		new(Theme, "Terminal theme", MokatermIcons.Palette),
		new(Clear, "Clear terminal", MokatermIcons.Eraser),
		new(Record, "Record to a file", MokatermIcons.Record),
		new(Broadcast, "Type into several sessions", MokatermIcons.Broadcast),
		new(Duplicate, "Duplicate session", MokaIcons.Content.Copy),
		new(ToolsMenu, "Tools menu", MokatermIcons.Tools),
		new(Disconnect, "Disconnect", MokatermIcons.Disconnect),
		new(Close, "Disconnect and close", MokatermIcons.Power),
	];

	/// <summary>The id a tool's own button is hidden under.</summary>
	public static string ForTool(SessionToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(tool);
		return ToolPrefix + tool.Key;
	}

	/// <summary>Whether <paramref name="id"/> is a tool's own button rather than one of the shell's.</summary>
	public static bool IsTool(string id)
	{
		ArgumentNullException.ThrowIfNull(id);
		return id.StartsWith(ToolPrefix, StringComparison.Ordinal);
	}

	/// <summary>Every button the setting can hide: the shell's, then one per tool, a tool on several protocols once.</summary>
	public static IReadOnlyList<SessionToolbarButton> All(IUiContributions contributions)
	{
		ArgumentNullException.ThrowIfNull(contributions);
		List<SessionToolbarButton> buttons = [.. BuiltIn];
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (SessionToolDescriptor tool in contributions.SessionTools)
		{
			string id = ForTool(tool);
			if (seen.Add(id))
			{
				buttons.Add(new SessionToolbarButton(id, tool.Title, tool.Icon));
			}
		}

		return buttons;
	}
}
