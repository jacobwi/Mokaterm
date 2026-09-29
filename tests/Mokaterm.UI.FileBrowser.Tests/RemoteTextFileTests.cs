using System.Text;
using Mokaterm.UI.FileBrowser.Editing;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>
/// The editor's contract with a remote file: what it opens, what it refuses, and that saving an untouched file writes
/// back the bytes that came in. A rewritten line ending or a dropped final newline would show as a whole-file change
/// in whatever the server has watching it.
/// </summary>
public sealed class RemoteTextFileTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData("line one\nline two\n")]
	[InlineData("line one\r\nline two\r\n")]
	[InlineData("no trailing newline")]
	[InlineData("crlf and no trailing newline\r\nsecond")]
	[InlineData("")]
	[InlineData("\n")]
	[InlineData("only a carriage return\rsecond\r")]
	public async Task AnUntouchedFile_IsWrittenBackByteForByte(string content)
	{
		byte[] original = Encoding.UTF8.GetBytes(content);

		(RemoteTextFile? file, TextFileRefusal refusal) = await ReadAsync(original);

		Assert.Equal(TextFileRefusal.None, refusal);
		Assert.NotNull(file);
		Assert.Equal(original, file.ToBytes(file.Text));
	}

	[Fact]
	public async Task ABomIsKept()
	{
		byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("with a mark\n")];

		(RemoteTextFile? file, _) = await ReadAsync(original);

		Assert.True(file!.HasBom);
		Assert.Equal("with a mark\n", file.Text);
		Assert.Equal(original, file.ToBytes(file.Text));
	}

	[Fact]
	public async Task EditingACrLfFile_KeepsCrLfOnEveryLine()
	{
		(RemoteTextFile? file, _) = await ReadAsync(Encoding.UTF8.GetBytes("one\r\ntwo\r\n"));

		// The text area hands back line feeds whatever the file used.
		byte[] saved = file!.ToBytes("one\ntwo\nthree\n");

		Assert.Equal("one\r\ntwo\r\nthree\r\n", Encoding.UTF8.GetString(saved));
	}

	[Fact]
	public async Task AFileWithNoFinalNewline_DoesNotGainOne()
	{
		(RemoteTextFile? file, _) = await ReadAsync(Encoding.UTF8.GetBytes("one\ntwo"));

		Assert.Equal("one\ntwo", Encoding.UTF8.GetString(file!.ToBytes("one\ntwo\n")));
	}

	[Fact]
	public async Task AFileWithAFinalNewline_KeepsIt()
	{
		(RemoteTextFile? file, _) = await ReadAsync(Encoding.UTF8.GetBytes("one\n"));

		Assert.Equal("one\ntwo\n", Encoding.UTF8.GetString(file!.ToBytes("one\ntwo")));
	}

	[Fact]
	public async Task ANulByte_IsRefusedAsBinary()
	{
		(RemoteTextFile? file, TextFileRefusal refusal) = await ReadAsync([0x68, 0x69, 0x00, 0x68, 0x69]);

		Assert.Null(file);
		Assert.Equal(TextFileRefusal.Binary, refusal);
	}

	[Fact]
	public async Task BytesThatAreNotUtf8_AreRefusedRatherThanSubstituted()
	{
		// Latin-1 "e acute". Decoding with replacement would write U+FFFD back over the server's byte.
		(RemoteTextFile? file, TextFileRefusal refusal) = await ReadAsync([0x63, 0x61, 0x66, 0xE9]);

		Assert.Null(file);
		Assert.Equal(TextFileRefusal.NotUtf8, refusal);
	}

	[Fact]
	public async Task PastTheCap_IsRefusedRatherThanCut()
	{
		(RemoteTextFile? file, TextFileRefusal refusal) = await ReadAsync(Encoding.UTF8.GetBytes(new string('a', 64)), maxBytes: 32);

		Assert.Null(file);
		Assert.Equal(TextFileRefusal.TooLarge, refusal);
	}

	[Fact]
	public async Task ExactlyTheCap_StillOpens()
	{
		(RemoteTextFile? file, TextFileRefusal refusal) = await ReadAsync(Encoding.UTF8.GetBytes(new string('a', 32)), maxBytes: 32);

		Assert.Equal(TextFileRefusal.None, refusal);
		Assert.Equal(32, file!.Text.Length);
	}

	[Theory]
	[InlineData("a\nb\nc\r\n", "\n")]
	[InlineData("a\r\nb\r\nc\n", "\r\n")]
	[InlineData("a\rb\rc\n", "\r")]
	[InlineData("no endings at all", "\n")]
	public void TheMajorityEndingWins(string content, string expected) =>
		Assert.Equal(expected, RemoteTextFile.DetectLineEnding(content));

	private static Task<(RemoteTextFile? File, TextFileRefusal Refusal)> ReadAsync(byte[] bytes, int maxBytes = 1024) =>
		RemoteTextFile.ReadAsync(new MemoryStream(bytes), maxBytes, Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);
}
