using Moka.Red.Core.Icons;
using Moka.Red.Icons;

namespace Mokaterm.UI.Common.Icons;

/// <summary>
/// Icons Mokaterm needs beyond <see cref="MokaIcons"/>, drawn the same way: single stroked path, 24x24 grid.
/// Common ones are re-exported so feature code has one place to look.
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

	public static readonly MokaIconDefinition Key = new("mt-key",
		"M15.5 7.5l2.3 2.3a1 1 0 0 0 1.4 0l2.1-2.1a1 1 0 0 0 0-1.4L19 4 M21 2l-9.6 9.6 M13 15.5a5.5 5.5 0 1 1-11 0 5.5 5.5 0 0 1 11 0z");

	public static readonly MokaIconDefinition Shield = new("mt-shield",
		"M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z");

	public static readonly MokaIconDefinition ShieldCheck = new("mt-shield-check",
		"M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z M9 12l2 2 4-4");

	/// <summary>Marks anything that runs as root.</summary>
	public static readonly MokaIconDefinition Root = new("mt-root",
		"M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z M12 8v4 M12 16h.01");

	public static readonly MokaIconDefinition User = new("mt-user",
		"M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2 M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0z");

	public static readonly MokaIconDefinition Connect = new("mt-connect",
		"M12 22v-5 M9 8V2 M15 8V2 M18 8v5a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V8z");

	public static readonly MokaIconDefinition Disconnect = new("mt-disconnect",
		"M19 5l3-3 M2 22l3-3 M6.3 20.3a2.4 2.4 0 0 0 3.4 0L12 18l-6-6-2.3 2.3a2.4 2.4 0 0 0 0 3.4z M7.5 13.5L10 11 M10.5 16.5L13 14 M12 6l6 6 2.3-2.3a2.4 2.4 0 0 0 0-3.4l-2.6-2.6a2.4 2.4 0 0 0-3.4 0z");

	public static readonly MokaIconDefinition Reconnect = MokaIcons.Action.Refresh;

	public static readonly MokaIconDefinition Download = MokaIcons.Action.Download;

	public static readonly MokaIconDefinition QuickConnect = new("mt-zap",
		"M13 2L3 14h9l-1 8 10-12h-9l1-8z");

	public static readonly MokaIconDefinition Globe = new("mt-globe",
		"M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0z M2 12h20 M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z");

	public static readonly MokaIconDefinition Monitor = new("mt-monitor",
		"M4 3h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M8 21h8 M12 17v4");

	public static readonly MokaIconDefinition FolderPlus = new("mt-folder-plus",
		"M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z M12 11v6 M9 14h6");

	public static readonly MokaIconDefinition FileUpload = new("mt-file-upload",
		"M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M12 18v-6 M9 15l3-3 3 3");

	public static readonly MokaIconDefinition FileDownload = new("mt-file-download",
		"M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M12 12v6 M9 15l3 3 3-3");

	public static readonly MokaIconDefinition Symlink = new("mt-symlink",
		"M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M8 16v-1a3 3 0 0 1 3-3h5 M13 9l3 3-3 3");

	public static readonly MokaIconDefinition Transfers = new("mt-transfers",
		"M7 21V3 M3 7l4-4 4 4 M17 3v18 M21 17l-4 4-4-4");

	public static readonly MokaIconDefinition History = new("mt-history",
		"M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5 M12 7v5l4 2");

	public static readonly MokaIconDefinition Broadcast = new("mt-broadcast",
		"M4.9 19.1a10 10 0 0 1 0-14.2 M7.8 16.2a6 6 0 0 1 0-8.4 M16.2 7.8a6 6 0 0 1 0 8.4 M19.1 4.9a10 10 0 0 1 0 14.2 M12 10a2 2 0 1 1 0 4 2 2 0 0 1 0-4z");

	/// <summary>Recording a session to a file: the usual ring with a dot in it.</summary>
	public static readonly MokaIconDefinition Record = new("mt-record",
		"M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0z M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0z");

	public static readonly MokaIconDefinition Keyboard = new("mt-keyboard",
		"M4 6h16a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z M6 10h.01 M10 10h.01 M14 10h.01 M18 10h.01 M8 14h8");

	public static readonly MokaIconDefinition PanelLeft = new("mt-panel-left",
		"M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M9 3v18");

	public static readonly MokaIconDefinition PanelBottom = new("mt-panel-bottom",
		"M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M3 15h18");

	public static readonly MokaIconDefinition SplitView = new("mt-split",
		"M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M12 3v18");

	public static readonly MokaIconDefinition Tag = new("mt-tag",
		"M12.6 2.6A2 2 0 0 0 11.2 2H4a2 2 0 0 0-2 2v7.2a2 2 0 0 0 .6 1.4l8.7 8.7a2.4 2.4 0 0 0 3.4 0l6.6-6.6a2.4 2.4 0 0 0 0-3.4z M7.5 7.5h.01");

	public static readonly MokaIconDefinition Sliders = new("mt-sliders",
		"M4 21v-7 M4 10V3 M12 21v-9 M12 8V3 M20 21v-5 M20 12V3 M1 14h6 M9 8h6 M17 16h6");

	public static readonly MokaIconDefinition Palette = new("mt-palette",
		"M12 2a10 10 0 1 0 0 20c1.1 0 2-.9 2-2 0-.5-.2-1-.5-1.3-.3-.4-.5-.8-.5-1.3 0-1.1.9-2 2-2h2.3c3 0 5.7-2.5 5.7-5.5C23 5.8 18 2 12 2z M6.5 11.5h.01 M9.5 7.5h.01 M14.5 7.5h.01 M17.5 11.5h.01");

	public static readonly MokaIconDefinition Command = new("mt-command",
		"M15 6v12a3 3 0 1 0 3-3H6a3 3 0 1 0 3 3V6a3 3 0 1 0-3 3h12a3 3 0 1 0-3-3");

	public static readonly MokaIconDefinition Eraser = new("mt-eraser",
		"M7 21l-4.3-4.3c-1-1-1-2.5 0-3.4l9.6-9.6c1-1 2.5-1 3.4 0l5.6 5.6c1 1 1 2.5 0 3.4L13 21 M22 21H7 M5 11l9 9");

	public static readonly MokaIconDefinition Maximize = new("mt-maximize",
		"M8 3H5a2 2 0 0 0-2 2v3 M21 8V5a2 2 0 0 0-2-2h-3 M3 16v3a2 2 0 0 0 2 2h3 M16 21h3a2 2 0 0 0 2-2v-3");

	public static readonly MokaIconDefinition Power = new("mt-power",
		"M12 2v10 M18.4 6.6a9 9 0 1 1-12.77.04");

	public static readonly MokaIconDefinition Modules = new("mt-modules",
		"M4 4h6v6H4z M14 4h6v6h-6z M4 14h6v6H4z M14 14h6v6h-6z");
}
