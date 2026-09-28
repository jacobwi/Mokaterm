using System.Text;
using Mokaterm.Abstractions.Platform;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The two rules the pick helper owns for every caller. Three editors had their own copy of this and three behaved
/// differently past their cap: one threw, and the other two cut the file and then handed the piece on as a whole color
/// scheme or a whole private key.
/// </summary>
public sealed class LocalFilePickTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ReadTextAsync_AFileOverTheCap_IsRefusedAndNotCut()
	{
		FakeFileAccess files = new(Text(new string('x', 200)), length: null);

		PickedFile<string> picked = await LocalFilePick.ReadTextAsync(files, 100, "a color scheme", Ct);

		Assert.False(picked.WasRead);
		Assert.Null(picked.Content);
		Assert.Equal("That file is too large to be a color scheme.", picked.Error);
		Assert.Equal(1, files.Released);
	}

	[Fact]
	public async Task ReadTextAsync_AFileThatSaysItsSize_IsRefusedBeforeItIsOpened()
	{
		FakeFileAccess files = new(Text(new string('x', 200)), length: 200);

		PickedFile<string> picked = await LocalFilePick.ReadTextAsync(files, 100, "a private key", Ct);

		Assert.Equal("That file is too large to be a private key.", picked.Error);
		Assert.Equal(0, files.Opened);
		Assert.Equal(1, files.Released);
	}

	[Fact]
	public async Task ReadTextAsync_AFileExactlyAtTheCap_IsReadWhole()
	{
		string content = new('k', 100);
		FakeFileAccess files = new(Text(content), length: 100);

		PickedFile<string> picked = await LocalFilePick.ReadTextAsync(files, 100, "a private key", Ct);

		Assert.True(picked.WasRead);
		Assert.Equal(content, picked.Content);
		Assert.Equal("picked.txt", picked.Name);
		Assert.Equal(1, files.Released);
	}

	[Fact]
	public async Task ReadBytesAsync_ReadsEveryChunk_AndRefusesOneByteTooMany()
	{
		byte[] content = new byte[300_000];
		Random.Shared.NextBytes(content);

		PickedFile<byte[]> whole = await LocalFilePick.ReadBytesAsync(new FakeFileAccess(content, length: null), content.Length, "a list", Ct);
		Assert.Equal(content, whole.Content);

		PickedFile<byte[]> refused = await LocalFilePick.ReadBytesAsync(
			new FakeFileAccess(content, length: null),
			content.Length - 1,
			"a list",
			Ct);
		Assert.Null(refused.Content);
		Assert.Equal("That file is too large to be a list.", refused.Error);
	}

	[Fact]
	public async Task ReadTextAsync_WhenThePickerIsClosed_SaysNothingAtAll()
	{
		FakeFileAccess files = new(content: null, length: null);

		PickedFile<string> picked = await LocalFilePick.ReadTextAsync(files, 100, "a color scheme", Ct);

		Assert.False(picked.WasRead);
		Assert.Null(picked.Error);
		Assert.Equal(1, files.Released);
	}

	[Fact]
	public async Task ReadTextAsync_WhenTheReadThrows_StillReleasesThePick()
	{
		FakeFileAccess files = new(Text("anything"), length: null, failOnOpen: true);

		await Assert.ThrowsAsync<IOException>(() => LocalFilePick.ReadTextAsync(files, 100, "a color scheme", Ct));

		Assert.Equal(1, files.Released);
	}

	private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);

	private sealed class FakeFileAccess(byte[]? content, long? length, bool failOnOpen = false) : ILocalFileAccess
	{
		public int Opened { get; private set; }

		public int Released { get; private set; }

		public bool CanPickFolders => false;

		public ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, CancellationToken cancellationToken = default)
		{
			if (content is null)
			{
				return ValueTask.FromResult<IReadOnlyList<LocalFileItem>>([]);
			}

			LocalFileItem file = new()
			{
				Name = "picked.txt",
				RelativePath = "picked.txt",
				Length = length,
				OpenReadAsync = _ =>
				{
					Opened++;
					return failOnOpen
						? throw new IOException("gone")
						: ValueTask.FromResult<Stream>(new MemoryStream(content, writable: false));
				},
			};

			return ValueTask.FromResult<IReadOnlyList<LocalFileItem>>([file]);
		}

		public ValueTask<IReadOnlyList<LocalFileItem>> PickFolderAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<IReadOnlyList<LocalFileItem>>([]);

		public ValueTask ReleaseAsync(IReadOnlyList<LocalFileItem> picked)
		{
			Released++;
			return ValueTask.CompletedTask;
		}

		public ValueTask<bool> SaveFileAsync(
			string suggestedName,
			long? length,
			Func<Stream, CancellationToken, Task> writeAsync,
			CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
	}
}
