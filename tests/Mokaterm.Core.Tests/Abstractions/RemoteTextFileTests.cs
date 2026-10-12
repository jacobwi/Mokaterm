using System.Text;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>
/// The contract between a remote file and every view that reads or edits it as text: what opens, what is refused, and
/// that saving an untouched file writes back the bytes that came in. A rewritten line ending or a dropped final newline
/// would show as a whole-file change in whatever the server has watching it.
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
	public async Task ABomAndCrLf_SurviveAnEditTogether()
	{
		byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("A=1\r\nB=2\r\n")];

		(RemoteTextFile? file, _) = await ReadAsync(original);

		Assert.Equal("A=1\nB=2\n", file!.Text);
		Assert.Equal(original, file.ToBytes("A=1\nB=2\n"));
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
	public async Task AnEmptyFile_GetsAFinalNewlineOnceItHasALine()
	{
		(RemoteTextFile? file, _) = await ReadAsync([]);

		Assert.True(file!.EndsWithNewLine);
		Assert.Equal("A=1\n", Encoding.UTF8.GetString(file.ToBytes("A=1")));
		Assert.Equal("A=1\n", Encoding.UTF8.GetString(file.ToBytes("A=1\n")));
	}

	[Fact]
	public async Task AnEmptiedFile_StaysEmpty()
	{
		(RemoteTextFile? file, _) = await ReadAsync(Encoding.UTF8.GetBytes("one\r\n"));

		Assert.Empty(file!.ToBytes(""));
	}

	[Theory]
	[InlineData("page one\fpage two\n")]
	[InlineData("next line\u0085in one\n")]
	[InlineData("line\u2028separator and\u2029paragraph\n")]
	[InlineData("vertical\vtab\n")]
	public async Task OnlyCrAndLfAreLineEndings_TheRestStayText(string content)
	{
		byte[] original = Encoding.UTF8.GetBytes(content);

		(RemoteTextFile? file, _) = await ReadAsync(original);

		Assert.Equal(content, file!.Text);
		Assert.Equal(original, file.ToBytes(file.Text));
	}

	[Fact]
	public async Task ANulByte_IsRefusedAsBinary()
	{
		(RemoteTextFile? file, TextFileRefusal refusal) = await ReadAsync([0x68, 0x69, 0x00, 0x68, 0x69]);

		Assert.Null(file);
		Assert.Equal(TextFileRefusal.Binary, refusal);
	}

	[Theory]
	[InlineData(new byte[] { 0x63, 0x61, 0x66, 0xE9 })]
	[InlineData(new byte[] { 0x61, 0xC3, 0x28 })]
	[InlineData(new byte[] { 0x61, 0xC3 })]
	public async Task BytesThatAreNotUtf8_AreRefusedRatherThanSubstituted(byte[] bytes)
	{
		// Decoding with replacement would write U+FFFD back over the server's bytes.
		(RemoteTextFile? file, TextFileRefusal refusal) = await ReadAsync(bytes);

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
		Assert.False(file.IsCut);
	}

	[Fact]
	public async Task AFileReadInManySmallPieces_ArrivesWhole()
	{
		// Past the first buffer, so it grows, and handed over a few bytes at a time the way a slow channel does.
		string content = string.Concat(Enumerable.Range(0, 20_000).Select(line => $"line {line}\n"));

		(RemoteTextFile? file, TextFileRefusal refusal) = await RemoteTextFile
			.ReadAsync(new TrickleStream(Encoding.UTF8.GetBytes(content), 7), 1024 * 1024, Ct)
			.WaitAsync(TimeSpan.FromSeconds(10), Ct);

		Assert.Equal(TextFileRefusal.None, refusal);
		Assert.Equal(content, file!.Text);
	}

	[Fact]
	public async Task ReadStart_KeepsTheStartOfALongerFile_AndSaysItIsCut()
	{
		(RemoteTextFile? file, TextFileRefusal refusal) = await ReadStartAsync(Encoding.UTF8.GetBytes("A=1\nB=2\nC=3\n"), maxBytes: 6);

		Assert.Equal(TextFileRefusal.None, refusal);
		Assert.True(file!.IsCut);
		Assert.Equal("A=1\nB=", file.Text);
	}

	[Fact]
	public async Task ReadStart_OfAFileThatFits_IsTheWholeFile()
	{
		(RemoteTextFile? file, _) = await ReadStartAsync(Encoding.UTF8.GetBytes("A=1\n"), maxBytes: 4);

		Assert.False(file!.IsCut);
		Assert.Equal("A=1\n", Encoding.UTF8.GetString(file.ToBytes(file.Text)));
	}

	[Fact]
	public async Task ACutFile_LosesOnlyTheCharacterTheCutSplit()
	{
		Assert.Equal("a", (await ReadStartAsync([0x61, 0xC3, 0xA9], maxBytes: 2)).File!.Text);
		Assert.Equal("a\u20AC", (await ReadStartAsync([0x61, 0xE2, 0x82, 0xAC, 0xF0, 0x9F, 0x98, 0x80], maxBytes: 7)).File!.Text);

		// Being cut does not make a binary file text.
		Assert.Equal(TextFileRefusal.NotUtf8, (await ReadStartAsync([0x61, 0xFF, 0x62, 0xC3, 0xA9], maxBytes: 4)).Refusal);
	}

	[Fact]
	public async Task ACutFile_IsNeverWrittenBack()
	{
		(RemoteTextFile? file, _) = await ReadStartAsync(Encoding.UTF8.GetBytes("A=1\nB=2\n"), maxBytes: 4);

		Assert.Throws<InvalidOperationException>(() => file!.ToBytes(file.Text));
	}

	[Fact]
	public void FromBytes_ReadsAWholeFile()
	{
		(RemoteTextFile? file, TextFileRefusal refusal) = RemoteTextFile.FromBytes([0xC3, 0xA9]);

		Assert.Equal(TextFileRefusal.None, refusal);
		Assert.Equal("\u00E9", file!.Text);
		Assert.False(file.HasBom);
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

	private static Task<(RemoteTextFile? File, TextFileRefusal Refusal)> ReadStartAsync(byte[] bytes, int maxBytes) =>
		RemoteTextFile.ReadStartAsync(new MemoryStream(bytes), maxBytes, Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);

	/// <summary>Hands out at most a few bytes per read, as a slow channel does.</summary>
	private sealed class TrickleStream(byte[] bytes, int piece) : MemoryStream(bytes, writable: false)
	{
		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
			base.ReadAsync(buffer[..Math.Min(buffer.Length, piece)], cancellationToken);
	}
}
