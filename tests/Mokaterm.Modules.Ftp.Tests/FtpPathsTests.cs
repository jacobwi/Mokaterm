using Mokaterm.Modules.Ftp.FileSystem;

namespace Mokaterm.Modules.Ftp.Tests;

public sealed class FtpPathsTests
{
	[Theory]
	[InlineData(null, "/home/alice")]
	[InlineData("", "/home/alice")]
	[InlineData("~", "/home/alice")]
	[InlineData("~/www", "/home/alice/www")]
	[InlineData("~/www/../logs/.", "/home/alice/logs")]
	[InlineData("public_html", "/home/alice/public_html")]
	[InlineData("../bob", "/home/bob")]
	[InlineData("/etc//nginx/", "/etc/nginx")]
	[InlineData("/..", "/")]
	[InlineData("~bob", "/home/alice/~bob")]
	public void Resolve_MakesPathsAbsolute(string? path, string expected) =>
		Assert.Equal(expected, FtpPaths.Resolve(path, "/home/alice"));

	[Theory]
	[InlineData("/home/alice", "/home/alice")]
	[InlineData("/home/alice/", "/home/alice")]
	[InlineData("./", "/")]
	[InlineData("DISK$USER:[ALICE]", "/")]
	[InlineData(null, "/")]
	public void ReadLoginDirectory_AcceptsOnlyPosixPaths(string? workingDirectory, string expected) =>
		Assert.Equal(expected, FtpPaths.ReadLoginDirectory(workingDirectory));

	[Theory]
	[InlineData("\"/home/alice\" is the current directory", "/home/alice")]
	[InlineData("\"/srv/say \"\"hi\"\"\" created", "/srv/say \"hi\"")]
	[InlineData("\"/\"", "/")]
	[InlineData("MKD command successful", null)]
	[InlineData("\"/unterminated", null)]
	[InlineData(null, null)]
	public void ParseQuotedPath_ReadsRfc959Quoting(string? message, string? expected) =>
		Assert.Equal(expected, FtpPaths.ParseQuotedPath(message));

	[Theory]
	[InlineData("/home/alice/notes.txt", true)]
	[InlineData("/tmp/evil\r\nDELE /etc/passwd", false)]
	[InlineData("/tmp/line\nbreak", false)]
	[InlineData("/tmp/nul\0", false)]
	public void IsSendable_RejectsCommandBreakingCharacters(string path, bool expected) =>
		Assert.Equal(expected, FtpPaths.IsSendable(path));
}
