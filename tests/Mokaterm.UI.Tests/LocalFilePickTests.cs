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
	public async Task ReadStreamAsync_Extensions_ReachThePickersOffer()
	{
		FakeFileAccess files = new(Text("{}"), length: null);

		await LocalFilePick.ReadStreamAsync(
			files,
			100,
			"a plugin package",
			(stream, token) => Task.FromResult(Array.Empty<byte>()),
			[".mokaplugin"],
			cancellationToken: Ct);

		Assert.Equal([".mokaplugin"], files.OfferedExtensions);
	}

	[Fact]
	public async Task ReadTextAsync_WithoutExtensions_PicksWithNoFilter()
	{
		FakeFileAccess files = new(Text("theme"), length: null);

		await LocalFilePick.ReadTextAsync(files, 100, "a color scheme", cancellationToken: Ct);

		Assert.Null(files.OfferedExtensions);
	}

	[Fact]
	public async Task ReadTextAsync_AFileOverTheCap_IsRefusedAndNotCut()
	{
		FakeFileAccess files = new(Text(new string('x', 200)), length: null);

		PickedFile<string> picked = await LocalFilePick.ReadTextAsync(files, 100, "a color scheme", cancellationToken: Ct);

		Assert.False(picked.WasRead);
		Assert.Null(picked.Content);
		Assert.Equal("That file is too large to be a color scheme.", picked.Error);
		Assert.Equal(1, files.Released);
	}

	[Fact]
	public async Task ReadTextAsync_AFileThatSaysItsSize_IsRefusedBeforeItIsOpened()
	{
		FakeFileAccess files = new(Text(new string('x', 200)), length: 200);

		PickedFile<string> picked = await LocalFilePick.ReadTextAsync(files, 100, "a private key", cancellationToken: Ct);

		Assert.Equal("That file is too large to be a private key.", picked.Error);
		Assert.Equal(0, files.Opened);
		Assert.Equal(1, files.Released);
	}

	[Fact]
	public async Task ReadTextAsync_AFileExactlyAtTheCap_IsReadWhole()
	{
		string content = new('k', 100);
		FakeFileAccess files = new(Text(content), length: 100);

		PickedFile<string> picked = await LocalFilePick.ReadTextAsync(files, 100, "a private key", cancellationToken: Ct);

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

		PickedFile<byte[]> whole = await LocalFilePick.ReadBytesAsync(new FakeFileAccess(content, length: null), content.Length, "a list", cancellationToken: Ct);
		Assert.Equal(content, whole.Content);

		PickedFile<byte[]> refused = await LocalFilePick.ReadBytesAsync(
			new FakeFileAccess(content, length: null),
			content.Length - 1,
			"a list",
			cancellationToken: Ct);
		Assert.Null(refused.Content);
		Assert.Equal("That file is too large to be a list.", refused.Error);
	}

	[Fact]
	public async Task ReadTextAsync_WhenThePickerIsClosed_SaysNothingAtAll()
	{
		FakeFileAccess files = new(content: null, length: null);

		PickedFile<string> picked = await LocalFilePick.ReadTextAsync(files, 100, "a color scheme", cancellationToken: Ct);

		Assert.False(picked.WasRead);
		Assert.Null(picked.Error);
		Assert.Equal(1, files.Released);
	}

	[Fact]
	public async Task ReadTextAsync_WhenTheReadThrows_StillReleasesThePick()
	{
		FakeFileAccess files = new(Text("anything"), length: null, failOnOpen: true);

		await Assert.ThrowsAsync<IOException>(() => LocalFilePick.ReadTextAsync(files, 100, "a color scheme", cancellationToken: Ct));

		Assert.Equal(1, files.Released);
	}

	[Fact]
	public async Task ReadStreamAsync_HandsTheReaderTheWholeFile_AndReleasesThePick()
	{
		byte[] content = new byte[300_000];
		Random.Shared.NextBytes(content);
		FakeFileAccess files = new(content, length: null);

		PickedFile<byte[]> picked = await LocalFilePick.ReadStreamAsync(files, content.Length, "a plugin package", CopyAsync, cancellationToken: Ct);

		Assert.True(picked.WasRead);
		Assert.Equal(content, picked.Content);
		Assert.Equal(1, files.Released);
	}

	// A package reader that meets the cap halfway through a zip says "not a zip file"; the user has to hear the real reason.
	[Fact]
	public async Task ReadStreamAsync_PastTheCap_IsRefusedWhateverTheReaderMadeOfIt()
	{
		FakeFileAccess files = new(new byte[200], length: null);

		PickedFile<byte[]> picked = await LocalFilePick.ReadStreamAsync<byte[]>(
			files,
			100,
			"a plugin package",
			async (stream, token) =>
			{
				try
				{
					return await CopyAsync(stream, token);
				}
				catch (IOException ex)
				{
					throw new InvalidDataException("Not a zip file.", ex);
				}
			},
			cancellationToken: Ct);

		Assert.False(picked.WasRead);
		Assert.Equal("That file is too large to be a plugin package.", picked.Error);
		Assert.Equal(1, files.Released);
	}

	[Fact]
	public async Task ReadStreamAsync_WhenTheReaderSwallowsTheCap_StillRefusesAndDisposesWhatItMade()
	{
		FakeFileAccess files = new(new byte[200], length: null);
		Disposable? made = null;

		PickedFile<Disposable> picked = await LocalFilePick.ReadStreamAsync(
			files,
			100,
			"a plugin package",
			async (stream, token) =>
			{
				try
				{
					await CopyAsync(stream, token);
				}
				catch (IOException)
				{
					// A careless reader that keeps what it had.
				}

				made = new Disposable();
				return made;
			},
			cancellationToken: Ct);

		Assert.Equal("That file is too large to be a plugin package.", picked.Error);
		Assert.NotNull(made);
		Assert.True(made.IsDisposed);
	}

	[Fact]
	public async Task ReadStreamAsync_AFileThatSaysItsSize_NeverReachesTheReader()
	{
		FakeFileAccess files = new(new byte[200], length: 200);
		bool read = false;

		PickedFile<byte[]> picked = await LocalFilePick.ReadStreamAsync(
			files,
			100,
			"a plugin package",
			(stream, token) =>
			{
				read = true;
				return CopyAsync(stream, token);
			},
			cancellationToken: Ct);

		Assert.Equal("That file is too large to be a plugin package.", picked.Error);
		Assert.False(read);
		Assert.Equal(0, files.Opened);
	}

	[Fact]
	public async Task ReadStreamAsync_TheReadersOwnComplaint_ReachesTheCaller()
	{
		FakeFileAccess files = new(new byte[50], length: null);

		InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() => LocalFilePick.ReadStreamAsync<byte[]>(
			files,
			100,
			"a plugin package",
			(_, _) => throw new InvalidDataException("plugin.json is missing."),
			cancellationToken: Ct));

		Assert.Equal("plugin.json is missing.", refused.Message);
		Assert.Equal(1, files.Released);
	}

	[Fact]
	public async Task ReadStreamAsync_WhenThePickerIsClosed_SaysNothingAtAll()
	{
		FakeFileAccess files = new(content: null, length: null);

		PickedFile<byte[]> picked = await LocalFilePick.ReadStreamAsync(files, 100, "a plugin package", CopyAsync, cancellationToken: Ct);

		Assert.False(picked.WasRead);
		Assert.Null(picked.Error);
		Assert.Equal(1, files.Released);
	}

	private static async Task<byte[]> CopyAsync(Stream stream, CancellationToken cancellationToken)
	{
		using MemoryStream copy = new();
		await stream.CopyToAsync(copy, cancellationToken);
		return copy.ToArray();
	}

	private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);

	private sealed class Disposable : IDisposable
	{
		public bool IsDisposed { get; private set; }

		public void Dispose() => IsDisposed = true;
	}

	private sealed class FakeFileAccess(byte[]? content, long? length, bool failOnOpen = false) : ILocalFileAccess
	{
		public int Opened { get; private set; }

		public int Released { get; private set; }

		/// <summary>What the last pick asked the picker to offer, when a pick has run.</summary>
		public IReadOnlyList<string>? OfferedExtensions { get; private set; }

		public bool CanPickFolders => false;

		public ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default)
		{
			OfferedExtensions = extensions;
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
