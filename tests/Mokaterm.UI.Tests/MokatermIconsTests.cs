using System.Reflection;
using Moka.Red.Core.Icons;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.UI.Tests;

public sealed class MokatermIconsTests
{
	// Gallery plugins are compiled against these fields, so each one stays a field of this name and type, whatever it
	// points at.
	private static readonly string[] PublishedToPlugins =
	[
		"Terminal", "Folder", "FolderOpen", "File", "Lock", "Unlock", "Server", "Key", "Shield", "ShieldCheck",
		"Root", "User", "Connect", "Disconnect", "Reconnect", "Download", "QuickConnect", "Globe", "Monitor",
		"FolderPlus", "FileUpload", "FileDownload", "FilePlus", "Cut", "Archive", "Symlink", "Transfers", "History",
		"Broadcast", "Record", "Keyboard", "PanelLeft", "PanelBottom", "SplitView", "Tag", "Sliders", "Palette",
		"Command", "Eraser", "Maximize", "Power", "Modules", "Plugin", "SinglePane", "SplitStacked", "SplitThree",
		"SplitGrid", "Play", "Pause", "Tools", "PopOut", "PopIn", "Pin", "MainWindow", "Fleet"
	];

	[Fact]
	public void EveryPublishedIcon_IsStillAStaticReadonlyField()
	{
		foreach (string name in PublishedToPlugins)
		{
			FieldInfo? field = typeof(MokatermIcons).GetField(name, BindingFlags.Public | BindingFlags.Static);
			Assert.NotNull(field);
			Assert.True(field.IsInitOnly, name);
			Assert.Equal(typeof(MokaIconDefinition), field.FieldType);
		}
	}

	[Fact]
	public void EveryIcon_IsAStrokedDrawingOnTheSharedGrid()
	{
		foreach (FieldInfo field in typeof(MokatermIcons).GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			MokaIconDefinition icon = (MokaIconDefinition)field.GetValue(null)!;
			Assert.False(string.IsNullOrWhiteSpace(icon.SvgPath), field.Name);
			Assert.Equal("0 0 24 24", icon.ViewBox);
			Assert.False(icon.Filled, field.Name);
		}
	}
}
