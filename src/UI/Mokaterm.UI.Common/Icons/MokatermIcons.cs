using Moka.Red.Core.Icons;
using Moka.Red.Icons;

namespace Mokaterm.UI.Common.Icons;

/// <summary>
/// Mokaterm's icons, under the names feature code and plugins know them by. Nearly all are <see cref="MokaIcons"/>;
/// the rest are drawn the same way: single stroked path, 24x24 grid.
/// </summary>
public static class MokatermIcons
{
	public static readonly MokaIconDefinition Terminal = MokaIcons.File.Terminal;

	public static readonly MokaIconDefinition Folder = MokaIcons.File.Folder;

	public static readonly MokaIconDefinition FolderOpen = MokaIcons.File.FolderOpen;

	public static readonly MokaIconDefinition File = MokaIcons.File.Document;

	public static readonly MokaIconDefinition Lock = MokaIcons.Toggle.Lock;

	public static readonly MokaIconDefinition Unlock = MokaIcons.Toggle.Unlock;

	public static readonly MokaIconDefinition Server = new("mt-server",
		"M4 3h16a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M4 13h16a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-6a1 1 0 0 1 1-1z M7 7h.01 M7 17h.01");

	public static readonly MokaIconDefinition Key = MokaIcons.Content.Key;

	public static readonly MokaIconDefinition Shield = MokaIcons.Status.Shield;

	public static readonly MokaIconDefinition ShieldCheck = MokaIcons.Status.ShieldCheck;

	/// <summary>Marks anything that runs as root.</summary>
	public static readonly MokaIconDefinition Root = MokaIcons.Status.ShieldAlert;

	public static readonly MokaIconDefinition User = MokaIcons.Content.User;

	public static readonly MokaIconDefinition Connect = MokaIcons.Content.Plug;

	public static readonly MokaIconDefinition Disconnect = MokaIcons.Content.Unplug;

	public static readonly MokaIconDefinition Reconnect = MokaIcons.Action.Refresh;

	public static readonly MokaIconDefinition Download = MokaIcons.Action.Download;

	public static readonly MokaIconDefinition QuickConnect = MokaIcons.Content.Zap;

	public static readonly MokaIconDefinition Globe = MokaIcons.Content.Globe;

	public static readonly MokaIconDefinition Monitor = MokaIcons.Content.Monitor;

	public static readonly MokaIconDefinition FolderPlus = MokaIcons.File.FolderPlus;

	public static readonly MokaIconDefinition FileUpload = MokaIcons.File.FileUpload;

	public static readonly MokaIconDefinition FileDownload = MokaIcons.File.FileDownload;

	public static readonly MokaIconDefinition FilePlus = MokaIcons.File.FilePlus;

	public static readonly MokaIconDefinition Cut = MokaIcons.Content.Cut;

	public static readonly MokaIconDefinition Archive = MokaIcons.File.Archive;

	public static readonly MokaIconDefinition Symlink = MokaIcons.File.FileSymlink;

	public static readonly MokaIconDefinition Transfers = MokaIcons.Navigation.ArrowUpDown;

	public static readonly MokaIconDefinition History = MokaIcons.Action.History;

	public static readonly MokaIconDefinition Broadcast = MokaIcons.Content.Broadcast;

	/// <summary>Recording a session to a file: the usual ring with a dot in it.</summary>
	public static readonly MokaIconDefinition Record = MokaIcons.Action.Record;

	public static readonly MokaIconDefinition Keyboard = new("mt-keyboard",
		"M4 6h16a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z M6 10h.01 M10 10h.01 M14 10h.01 M18 10h.01 M8 14h8");

	public static readonly MokaIconDefinition PanelLeft = MokaIcons.Navigation.PanelLeft;

	public static readonly MokaIconDefinition PanelBottom = MokaIcons.Navigation.PanelBottom;

	public static readonly MokaIconDefinition SplitView = MokaIcons.Navigation.SplitView;

	public static readonly MokaIconDefinition Tag = MokaIcons.Content.Tag;

	public static readonly MokaIconDefinition Sliders = MokaIcons.Action.Sliders;

	public static readonly MokaIconDefinition Palette = MokaIcons.Content.Palette;

	public static readonly MokaIconDefinition Command = MokaIcons.Content.Command;

	public static readonly MokaIconDefinition Eraser = MokaIcons.Action.Eraser;

	public static readonly MokaIconDefinition Maximize = MokaIcons.Navigation.Maximize;

	public static readonly MokaIconDefinition Power = MokaIcons.Action.Power;

	public static readonly MokaIconDefinition Modules = MokaIcons.Navigation.Grid;

	/// <summary>A plugin package, drawn as the box a <c>.mokaplugin</c> file arrives in.</summary>
	public static readonly MokaIconDefinition Plugin = MokaIcons.File.Package;

	/// <summary>One pane, drawn as the frame <see cref="SplitView"/> divides.</summary>
	public static readonly MokaIconDefinition SinglePane = MokaIcons.Toggle.Square;

	public static readonly MokaIconDefinition SplitStacked = MokaIcons.Navigation.SplitStacked;

	/// <summary>One large pane on the left and two stacked on its right.</summary>
	public static readonly MokaIconDefinition SplitThree = MokaIcons.Navigation.SplitThree;

	public static readonly MokaIconDefinition SplitGrid = MokaIcons.Navigation.SplitGrid;

	public static readonly MokaIconDefinition Play = MokaIcons.Action.Play;

	public static readonly MokaIconDefinition Pause = new("mt-pause",
		"M8 5v14 M16 5v14");

	/// <summary>The session toolbar's menu of tools a module added.</summary>
	public static readonly MokaIconDefinition Tools = MokaIcons.Content.Wrench;

	/// <summary>Moves a tool out of its dialog into a window of its own.</summary>
	public static readonly MokaIconDefinition PopOut = MokaIcons.Content.ExternalLink;

	/// <summary>Moves a tool out of its own window, back into a dialog in the main window.</summary>
	public static readonly MokaIconDefinition PopIn = MokaIcons.Navigation.Dock;

	/// <summary>Keeps a window above every other window.</summary>
	public static readonly MokaIconDefinition Pin = MokaIcons.Toggle.Pin;

	/// <summary>The main window, brought in front of a tool's own window.</summary>
	public static readonly MokaIconDefinition MainWindow = MokaIcons.Navigation.AppWindow;

	/// <summary>The fleet overview: every saved machine in one table.</summary>
	public static readonly MokaIconDefinition Fleet = MokaIcons.Navigation.Dashboard;
}
