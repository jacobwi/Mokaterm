using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// A file read a piece at a time over a scripted server: pieces grow to the cap from where they start, a short one or the
/// size the version gives ends the file without another request, and past the warm-up two pieces are on their way at
/// once and never more. A file that changes, a piece that fails, a cancelled read and a disposed stream each end the
/// right pieces at the right read.
/// </summary>
public sealed class PieceReadStreamTests
{
	private const string LogPath = "/var/log/app.log";

	// Where each piece starts: 256 KiB, then 1 MiB, then 4 MiB each.
	private const long Piece2 = PieceReadStream.FirstPieceBytes;
	private const long Piece3 = Piece2 + (1024 * 1024);
	private const long Piece4 = Piece3 + PieceReadStream.MaxPieceBytes;
	private const long Piece5 = Piece4 + PieceReadStream.MaxPieceBytes;

	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Pieces_GrowToTheCap_AndAShortOneIsTheEnd()
	{
		ScriptedFile file = new(Bytes((10 * 1024 * 1024) + 7));

		await using PieceReadStream stream = await OpenAsync(file, 0);
		using MemoryStream read = new();
		await stream.CopyToAsync(read, Ct).WaitAsync(Limit, Ct);

		// Past the warm-up two pieces run at once, so they reach the server in either order.
		Assert.Equal(file.Content, read.ToArray());
		Assert.Equal(
			[
				(0L, PieceReadStream.FirstPieceBytes),
				(Piece2, 1024 * 1024),
				(Piece3, PieceReadStream.MaxPieceBytes),
				(Piece4, PieceReadStream.MaxPieceBytes),
				(Piece5, PieceReadStream.MaxPieceBytes),
			],
			file.Asked.OrderBy(piece => piece.Start));
	}

	[Fact]
	public async Task AFileOfExactlyOnePiece_EndsThere_WhenTheVersionSaysHowLongItIs()
	{
		ScriptedFile file = new(Bytes(PieceReadStream.FirstPieceBytes));

		await using PieceReadStream stream = await OpenAsync(file, 0);
		using MemoryStream read = new();
		await stream.CopyToAsync(read, Ct).WaitAsync(Limit, Ct);

		Assert.Equal(file.Content, read.ToArray());
		Assert.Single(file.Asked);
		Assert.Equal(0, await ReadOnceAsync(stream, new byte[16]));
		Assert.Single(file.Asked);
	}

	// Without a version a full piece cannot tell its end from the file's; the next piece comes back empty.
	[Fact]
	public async Task AFileOfExactlyOnePiece_WithoutAVersion_EndsAtAnEmptyPiece()
	{
		ScriptedFile file = new(Bytes(PieceReadStream.FirstPieceBytes)) { WithVersions = false };

		await using PieceReadStream stream = await OpenAsync(file, 0);
		using MemoryStream read = new();
		await stream.CopyToAsync(read, Ct).WaitAsync(Limit, Ct);

		Assert.Equal(file.Content, read.ToArray());
		Assert.Equal(2, file.Asked.Count);
	}

	[Fact]
	public async Task AnEmptyFile_IsOnePieceAndNothingElse()
	{
		ScriptedFile file = new([]);

		await using PieceReadStream stream = await OpenAsync(file, 0);

		Assert.Equal(0, await ReadOnceAsync(stream, new byte[16]));
		Assert.Single(file.Asked);
	}

	[Fact]
	public async Task AStartPastZero_IsWhereTheFirstPieceBegins()
	{
		ScriptedFile file = new(Bytes(1000));

		await using PieceReadStream stream = await OpenAsync(file, 600);
		using MemoryStream read = new();
		await stream.CopyToAsync(read, Ct).WaitAsync(Limit, Ct);

		Assert.Equal(file.Content.AsSpan(600).ToArray(), read.ToArray());
		Assert.Equal(600, file.Asked[0].Start);
	}

	[Fact]
	public async Task AServerThatSendsMoreThanAsked_IsCutToThePiece()
	{
		byte[] content = Bytes(PieceReadStream.FirstPieceBytes * 2);
		string version = $"2049 131 {content.Length} 1760000000";
		await using PieceReadStream stream = await PieceReadStream.OpenAsync(
			(start, _, _) => Task.FromResult(new FilePiece(version, content.AsMemory((int)start))),
			LogPath,
			0,
			Ct).WaitAsync(Limit, Ct);

		byte[] first = new byte[PieceReadStream.FirstPieceBytes + 10];
		int read = await ReadOnceAsync(stream, first);

		Assert.Equal(PieceReadStream.FirstPieceBytes, read);
	}

	[Fact]
	public async Task AFirstPieceThatFails_IsTheOpensFailure()
	{
		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(() => PieceReadStream.OpenAsync(
			(_, _, _) => Task.FromException<FilePiece>(new RemoteFileSystemException(RemoteFileErrorKind.NotFound, "/nope does not exist.", "/nope")),
			"/nope",
			0,
			Ct).WaitAsync(Limit, Ct));

		Assert.Equal(RemoteFileErrorKind.NotFound, failure.Kind);
	}

	[Fact]
	public async Task ACancelledOpen_EndsItsPiece_BeforeItReturns()
	{
		ScriptedFile file = new(Bytes(1000));
		TaskCompletionSource gate = file.HoldFrom(0);
		using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

		Task<PieceReadStream> opening = PieceReadStream.OpenAsync(file.ReadPieceAsync, LogPath, 0, cancel.Token);
		await Eventually.TrueAsync(() => file.Running == 1, "the first piece is on its way");
		await cancel.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening.WaitAsync(Limit, Ct));
		Assert.Equal(0, file.Running);
		Assert.True(file.Tokens[0].IsCancellationRequested);
		gate.TrySetResult();
	}

	[Fact]
	public async Task AFileThatGrowsBetweenPieces_FailsTheRead()
	{
		ScriptedFile file = new(Bytes(300 * 1024));
		await using PieceReadStream stream = await OpenAsync(file, 0);
		byte[] buffer = new byte[PieceReadStream.FirstPieceBytes];
		Assert.Equal(buffer.Length, await ReadFullyAsync(stream, buffer));

		file.Replace([.. file.Content, .. Bytes(5000)]);
		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(async () => await ReadFullyAsync(stream, buffer));

		Assert.Equal(RemoteFileErrorKind.Unknown, failure.Kind);
		Assert.Equal(LogPath, failure.Path);
		Assert.Contains("changed on the server while it was being read", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AFileThatShrinksBetweenPieces_FailsTheRead()
	{
		ScriptedFile file = new(Bytes(2 * 1024 * 1024));
		await using PieceReadStream stream = await OpenAsync(file, 0);
		byte[] buffer = new byte[PieceReadStream.FirstPieceBytes];
		Assert.Equal(buffer.Length, await ReadFullyAsync(stream, buffer));

		// Truncated and rewritten shorter, as logrotate's copytruncate leaves a log.
		file.Replace(Bytes(1024 * 1024));
		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(async () => await ReadFullyAsync(stream, buffer));

		Assert.Contains("changed on the server", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AFileReplacedBetweenPieces_FailsTheRead_EvenAtTheSameSize()
	{
		ScriptedFile file = new(Bytes(600 * 1024));
		await using PieceReadStream stream = await OpenAsync(file, 0);
		byte[] buffer = new byte[PieceReadStream.FirstPieceBytes];
		Assert.Equal(buffer.Length, await ReadFullyAsync(stream, buffer));

		file.Replace([.. file.Content]);
		await Assert.ThrowsAsync<RemoteFileSystemException>(async () => await ReadFullyAsync(stream, buffer));
	}

	[Fact]
	public async Task APieceCutShortOfTheSizeTheVersionGave_FailsTheRead()
	{
		ScriptedFile file = new(Bytes(600 * 1024)) { CutFrom = Piece2 };
		await using PieceReadStream stream = await OpenAsync(file, 0);
		byte[] buffer = new byte[PieceReadStream.FirstPieceBytes];
		Assert.Equal(buffer.Length, await ReadFullyAsync(stream, buffer));

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(async () => await ReadFullyAsync(stream, buffer));

		Assert.Contains("changed on the server", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AServerThatCannotSayWhichVersion_IsReadWithoutTheCheck()
	{
		ScriptedFile file = new(Bytes(300 * 1024)) { WithVersions = false };
		await using PieceReadStream stream = await OpenAsync(file, 0);
		byte[] buffer = new byte[PieceReadStream.FirstPieceBytes];
		Assert.Equal(buffer.Length, await ReadFullyAsync(stream, buffer));

		file.Replace([.. file.Content, .. Bytes(10)]);
		using MemoryStream rest = new();
		await stream.CopyToAsync(rest, Ct).WaitAsync(Limit, Ct);

		Assert.Equal((300 * 1024) + 10 - PieceReadStream.FirstPieceBytes, rest.Length);
	}

	[Fact]
	public async Task APreview_NeverPaysForAPieceItDoesNotRead()
	{
		ScriptedFile file = new(Bytes(20 * 1024 * 1024));
		await using PieceReadStream stream = await OpenAsync(file, 0);

		// What the editor and the volume viewer read: the first 256 KiB and one byte to know there is more.
		byte[] start = new byte[PieceReadStream.FirstPieceBytes + 1];
		Assert.Equal(start.Length, await ReadFullyAsync(stream, start));
		await Eventually.StaysTrueAsync(() => file.Asked.Count == 2, "nothing past the second piece is asked for");
	}

	[Fact]
	public async Task ReadAhead_PastTheWarmUp_KeepsTwoPiecesOnTheirWayAtOnce()
	{
		ScriptedFile file = new(Bytes(20 * 1024 * 1024));
		await using PieceReadStream stream = await OpenAsync(file, 0);
		await ReadThroughWarmUpAsync(stream);
		Assert.Equal(2, file.Asked.Count);

		// The reader asks for the third piece: the fourth is asked for while the third is still on its way.
		TaskCompletionSource gate = file.HoldFrom(Piece3);
		Task<int> waiting = ReadOnceAsync(stream, new byte[16]);
		await Eventually.TrueAsync(() => file.Running == 2, "two pieces are on their way at once");
		Assert.Equal([Piece3, Piece4], file.Asked.Skip(2).Select(piece => piece.Start).Order());
		Assert.False(waiting.IsCompleted);

		gate.SetResult();
		Assert.Equal(16, await waiting);
		using MemoryStream rest = new();
		await stream.CopyToAsync(rest, Ct).WaitAsync(Limit, Ct);

		Assert.Equal(file.Content.AsSpan((int)Piece3 + 16).ToArray(), rest.ToArray());
		Assert.Equal(2, file.MaxRunning);
	}

	[Fact]
	public async Task ReadAhead_AReaderThatStopsReading_HoldsTwoPiecesAtMost()
	{
		ScriptedFile file = new(Bytes(40 * 1024 * 1024));

		// One that never reads holds the piece it opened with.
		await using (PieceReadStream idle = await OpenAsync(file, 0))
		{
			await Eventually.StaysTrueAsync(() => file.Asked.Count == 1, "an idle reader asks for nothing more");
		}

		file.ForgetAsked();
		await using PieceReadStream stream = await OpenAsync(file, 0);
		await ReadThroughWarmUpAsync(stream);
		Assert.Equal(16, await ReadOnceAsync(stream, new byte[16]));

		// The piece being read and the one after it, and then nothing, however long the reader waits.
		await Eventually.TrueAsync(() => file.Asked.Count == 4, "the piece after the one being read is asked for");
		await Eventually.StaysTrueAsync(() => file.Asked.Count == 4, "a reader that stopped holds two pieces");
		long read = Piece3 + 16;
		long asked = file.Asked.Max(piece => piece.Start + piece.Count);
		Assert.InRange(asked - read, 0, PieceReadStream.MaxPiecesHeld * (long)PieceReadStream.MaxPieceBytes);
	}

	[Fact]
	public async Task ReadAhead_ACancelledRead_StopsWaiting_AndDisposingEndsThePiecesOnTheirWay()
	{
		ScriptedFile file = new(Bytes(20 * 1024 * 1024));
		PieceReadStream stream = await OpenAsync(file, 0);
		await ReadThroughWarmUpAsync(stream);
		TaskCompletionSource gate = file.HoldFrom(Piece3);
		using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

		Task<int> waiting = stream.ReadAsync(new byte[16], cancel.Token).AsTask();
		await Eventually.TrueAsync(() => file.Running == 2, "the read waits on two pieces on their way");
		await cancel.CancelAsync();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(Limit, Ct));

		// The pieces are the stream's, so they go on: a read that comes back would take them.
		Assert.Equal(2, file.Running);
		Assert.All(file.Tokens.Skip(2), token => Assert.False(token.IsCancellationRequested));

		await stream.DisposeAsync().AsTask().WaitAsync(Limit, Ct);

		Assert.Equal(0, file.Running);
		Assert.All(file.Tokens.Skip(2), token => Assert.True(token.IsCancellationRequested));
		Assert.Equal(4, file.Asked.Count);
		gate.TrySetResult();
	}

	[Fact]
	public async Task ReadAhead_DisposingWhileAPieceIsOnItsWay_EndsIt()
	{
		ScriptedFile file = new(Bytes(20 * 1024 * 1024));
		PieceReadStream stream = await OpenAsync(file, 0);
		await ReadThroughWarmUpAsync(stream);

		// The third piece arrives and is being read; the fourth stays on its way with no read waiting for it.
		TaskCompletionSource gate = file.HoldFrom(Piece4);
		Assert.Equal(16, await ReadOnceAsync(stream, new byte[16]));
		await Eventually.TrueAsync(() => file.Running == 1, "the fourth piece is on its way");

		stream.Dispose();

		await Eventually.TrueAsync(() => file.Running == 0, "disposing ends the piece on its way");
		Assert.True(file.Tokens[^1].IsCancellationRequested);
		Assert.Equal(4, file.Asked.Count);
		gate.TrySetResult();
	}

	[Fact]
	public async Task ReadAhead_ASudoThatRefusesTheNextPiece_FailsOnlyTheReadThatWouldTakeIt()
	{
		ScriptedFile file = new(Bytes(20 * 1024 * 1024))
		{
			FailFrom = (Piece4, new RemoteFileSystemException(RemoteFileErrorKind.ElevationFailed, "sudo did not accept the password for abc@web01.", LogPath)),
		};
		await using PieceReadStream stream = await OpenAsync(file, 0);
		await ReadThroughWarmUpAsync(stream);

		// The fourth piece fails while the third is read, and the third reads to its last byte all the same.
		byte[] third = new byte[PieceReadStream.MaxPieceBytes];
		Assert.Equal(16, await ReadOnceAsync(stream, third.AsMemory(0, 16)));
		await Eventually.TrueAsync(() => file.Failed == 1, "the fourth piece failed");
		Assert.Equal(third.Length - 16, await ReadFullyAsync(stream, third.AsMemory(16)));

		RemoteFileSystemException refused = await Assert.ThrowsAsync<RemoteFileSystemException>(async () => await ReadOnceAsync(stream, new byte[16]));
		RemoteFileSystemException again = await Assert.ThrowsAsync<RemoteFileSystemException>(async () => await ReadOnceAsync(stream, new byte[16]));

		Assert.Equal(RemoteFileErrorKind.ElevationFailed, refused.Kind);
		Assert.Same(refused, again);
		Assert.Equal(4, file.Asked.Count);
	}

	[Fact]
	public async Task ReadAhead_ANextPieceFromAnotherVersion_FailsOnlyTheReadThatWouldTakeIt()
	{
		ScriptedFile file = new(Bytes(20 * 1024 * 1024)) { ChangedFrom = Piece4 };
		await using PieceReadStream stream = await OpenAsync(file, 0);
		await ReadThroughWarmUpAsync(stream);

		// The fourth piece is back, from another version, while the third is read, and the third reads whole all the same.
		byte[] third = new byte[PieceReadStream.MaxPieceBytes];
		Assert.Equal(16, await ReadOnceAsync(stream, third.AsMemory(0, 16)));
		await Eventually.TrueAsync(() => file.Answered == 4, "the fourth piece came back");
		Assert.Equal(third.Length - 16, await ReadFullyAsync(stream, third.AsMemory(16)));
		Assert.Equal(file.Content.AsSpan((int)Piece3, third.Length).ToArray(), third);

		RemoteFileSystemException changed = await Assert.ThrowsAsync<RemoteFileSystemException>(async () => await ReadOnceAsync(stream, new byte[16]));

		Assert.Contains("changed on the server", changed.Message, StringComparison.Ordinal);

		// Nothing is asked for past a piece that already says the read is over.
		Assert.Equal(4, file.Asked.Count);
	}

	[Theory]
	[InlineData((2 * 1024 * 1024) + 5, 3)]
	[InlineData((int)Piece3, 2)]
	[InlineData((int)Piece5 + 7, 5)]
	[InlineData((int)Piece5, 4)]
	public async Task ReadAhead_AsksForNothingPastTheEnd(int length, int pieces)
	{
		ScriptedFile file = new(Bytes(length));
		await using PieceReadStream stream = await OpenAsync(file, 0);
		using MemoryStream read = new();
		await stream.CopyToAsync(read, Ct).WaitAsync(Limit, Ct);

		Assert.Equal(file.Content, read.ToArray());
		Assert.Equal(0, await ReadOnceAsync(stream, new byte[16]));
		await Eventually.StaysTrueAsync(() => file.Asked.Count == pieces, "nothing is asked for past the end");
		Assert.All(file.Asked, piece => Assert.True(piece.Start < length || piece.Start == 0));
	}

	// Without a version the stream cannot know the end, so it reads ahead only past a piece that came back whole.
	[Fact]
	public async Task ReadAhead_WithoutAVersion_NeverAsksPastAShortPiece()
	{
		ScriptedFile file = new(Bytes((int)Piece4 + 100)) { WithVersions = false };
		await using PieceReadStream stream = await OpenAsync(file, 0);
		using MemoryStream read = new();
		await stream.CopyToAsync(read, Ct).WaitAsync(Limit, Ct);

		Assert.Equal(file.Content, read.ToArray());
		await Eventually.StaysTrueAsync(() => file.Asked.Count == 4, "nothing is asked for past the short piece");
		Assert.Equal(1, file.MaxRunning);
	}

	[Fact]
	public async Task TwoReadersAhead_FillTheLongShare_AndQuickCallsStillGetThrough()
	{
		ExecChannelBudget budget = new();
		ScriptedFile file = new(Bytes(40 * 1024 * 1024)) { Budget = budget };
		await using PieceReadStream first = await OpenAsync(file, 0);
		await using PieceReadStream second = await OpenAsync(file, 0);
		await ReadThroughWarmUpAsync(first);
		await ReadThroughWarmUpAsync(second);
		TaskCompletionSource gate = file.HoldFrom(Piece3);

		Task<int> one = ReadOnceAsync(first, new byte[16]);
		Task<int> two = ReadOnceAsync(second, new byte[16]);
		await Eventually.TrueAsync(() => budget.InUse == ExecChannelBudget.Capacity - ExecChannelBudget.ReservedForShort, "both readers hold two slots");

		// The slots kept for quick calls are still there, and a third reader waits like any long call.
		using (ExecSlot quick = await budget.AcquireAsync(runsLong: false, Ct).AsTask().WaitAsync(Limit, Ct))
		{
			Task<PieceReadStream> third = PieceReadStream.OpenAsync(file.ReadPieceAsync, LogPath, 0, Ct);
			await Eventually.TrueAsync(() => budget.Waiting == 1, "the third reader waits for a slot");
			Assert.False(third.IsCompleted);

			gate.SetResult();
			Assert.Equal(16, await one.WaitAsync(Limit, Ct));
			Assert.Equal(16, await two.WaitAsync(Limit, Ct));
			await using PieceReadStream opened = await third.WaitAsync(Limit, Ct);
		}
	}

	[Fact]
	public async Task ASudoThatStopsLettingTheLoginIn_FailsTheReadAsElevationFailed()
	{
		ScriptedFile file = new(Bytes(600 * 1024));
		await using PieceReadStream stream = await OpenAsync(file, 0);
		byte[] buffer = new byte[PieceReadStream.FirstPieceBytes];
		Assert.Equal(buffer.Length, await ReadFullyAsync(stream, buffer));

		file.FailFrom = (0, new RemoteFileSystemException(RemoteFileErrorKind.ElevationFailed, "sudo did not accept the password for abc@web01.", LogPath));
		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(async () => await ReadFullyAsync(stream, buffer));

		Assert.Equal(RemoteFileErrorKind.ElevationFailed, failure.Kind);
	}

	[Fact]
	public async Task ASynchronousRead_WorksToo()
	{
		ScriptedFile file = new(Bytes(6 * 1024 * 1024));
		await using PieceReadStream stream = await OpenAsync(file, 0);
		using MemoryStream read = new();

		stream.CopyTo(read);

		Assert.Equal(file.Content, read.ToArray());
	}

	[Fact]
	public void APiece_IsItsVersionThenItsBytes()
	{
		byte[] payload = [.. "2049 131 300004 1760000000"u8, 0, .. "data\0more"u8];

		FilePiece piece = FilePiece.Parse(payload, LogPath);

		Assert.Equal("2049 131 300004 1760000000", piece.Version);
		Assert.Equal(300_004, piece.Size);
		Assert.Equal("data\0more"u8.ToArray(), piece.Bytes.ToArray());
		Assert.Equal("more"u8.ToArray(), piece.Skip(5).Bytes.ToArray());
		Assert.True(piece.Skip(100).Bytes.IsEmpty);
		Assert.Equal("", FilePiece.Parse(new byte[] { 0, 1, 2 }, LogPath).Version);
	}

	[Theory]
	[InlineData("")]
	[InlineData("2049 131 300004")]
	[InlineData("2049 131 -4 1760000000")]
	[InlineData("2049 131 3e4 1760000000")]
	public void AVersionThatIsNotFourNumbers_GivesNoSize(string version) =>
		Assert.Null(new FilePiece(version, ReadOnlyMemory<byte>.Empty).Size);

	[Fact]
	public void APieceWithoutAVersion_IsNotTheScripts()
	{
		byte[] noVersion = [.. Enumerable.Repeat((byte)'x', 1000)];

		RemoteFileSystemException failure = Assert.Throws<RemoteFileSystemException>(() => FilePiece.Parse(noVersion, LogPath));

		Assert.Contains("in a form this app does not read", failure.Message, StringComparison.Ordinal);
	}

	private static Task<PieceReadStream> OpenAsync(ScriptedFile file, long start) =>
		PieceReadStream.OpenAsync(file.ReadPieceAsync, LogPath, start, Ct).WaitAsync(Limit, Ct);

	/// <summary>Reads the first two pieces whole, after which the stream reads ahead.</summary>
	private static async Task ReadThroughWarmUpAsync(Stream stream)
	{
		byte[] warmUp = new byte[Piece3];
		Assert.Equal(warmUp.Length, await ReadFullyAsync(stream, warmUp));
	}

	private static Task<int> ReadOnceAsync(Stream stream, Memory<byte> buffer) => stream.ReadAsync(buffer, Ct).AsTask().WaitAsync(Limit, Ct);

	private static async Task<int> ReadFullyAsync(Stream stream, Memory<byte> buffer)
	{
		int total = 0;
		int read;
		while (total < buffer.Length && (read = await ReadOnceAsync(stream, buffer[total..])) > 0)
		{
			total += read;
		}

		return total;
	}

	private static byte[] Bytes(int length)
	{
		byte[] bytes = new byte[length];
		for (int index = 0; index < length; index++)
		{
			bytes[index] = (byte)(index * 31 % 251);
		}

		return bytes;
	}

	/// <summary>
	/// A file on a scripted server: every piece is answered from its current content with its current version, which
	/// changes whenever the test replaces the content, the way stat's device, inode, size and time would. Pieces run on
	/// the thread pool, so everything the test reads back is kept under a lock.
	/// </summary>
	private sealed class ScriptedFile(byte[] content)
	{
		private readonly Lock _lock = new();
		private readonly List<(long Start, int Count)> _asked = [];
		private readonly List<CancellationToken> _tokens = [];
		private int _version = 1;
		private int _running;
		private int _maxRunning;
		private int _failed;
		private int _answered;
		private (long Start, TaskCompletionSource Gate)? _hold;

		public byte[] Content { get; private set; } = content;

		public bool WithVersions { get; init; } = true;

		/// <summary>Pieces from this offset on fail with the exception, as sudo refusing would.</summary>
		public (long Start, RemoteFileSystemException Failure)? FailFrom { get; set; }

		/// <summary>Pieces from this offset on come from another version of the file.</summary>
		public long? ChangedFrom { get; init; }

		/// <summary>Pieces from this offset on come back short, without the version changing.</summary>
		public long? CutFrom { get; init; }

		/// <summary>When set, every piece holds a slot of it for as long as it runs, as an exec channel does.</summary>
		public ExecChannelBudget? Budget { get; init; }

		public IReadOnlyList<(long Start, int Count)> Asked
		{
			get
			{
				lock (_lock)
				{
					return [.. _asked];
				}
			}
		}

		public IReadOnlyList<CancellationToken> Tokens
		{
			get
			{
				lock (_lock)
				{
					return [.. _tokens];
				}
			}
		}

		public int Running => Volatile.Read(ref _running);

		public int MaxRunning => Volatile.Read(ref _maxRunning);

		public int Failed => Volatile.Read(ref _failed);

		/// <summary>Pieces that came back with bytes rather than failing.</summary>
		public int Answered => Volatile.Read(ref _answered);

		public void ForgetAsked()
		{
			lock (_lock)
			{
				_asked.Clear();
				_tokens.Clear();
			}
		}

		/// <summary>Pieces from <paramref name="start"/> on wait for the gate (or their token) before they answer.</summary>
		public TaskCompletionSource HoldFrom(long start)
		{
			TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
			lock (_lock)
			{
				_hold = (start, gate);
			}

			return gate;
		}

		public void Replace(byte[] content)
		{
			lock (_lock)
			{
				Content = content;
				_version++;
			}
		}

		public async Task<FilePiece> ReadPieceAsync(long start, int count, CancellationToken cancellationToken)
		{
			(long Start, TaskCompletionSource Gate)? hold;
			lock (_lock)
			{
				_asked.Add((start, count));
				_tokens.Add(cancellationToken);
				hold = _hold;
			}

			ExecSlot? slot = Budget is null ? null : await Budget.AcquireAsync(runsLong: true, cancellationToken);
			int running = Interlocked.Increment(ref _running);
			InterlockedMax(ref _maxRunning, running);
			try
			{
				if (hold is { } held && start >= held.Start)
				{
					await held.Gate.Task.WaitAsync(cancellationToken);
				}

				if (FailFrom is { } failure && start >= failure.Start)
				{
					_ = Interlocked.Increment(ref _failed);
					throw failure.Failure;
				}

				lock (_lock)
				{
					int version = ChangedFrom is { } changed && start >= changed ? _version + 1 : _version;
					int from = (int)Math.Min(start, Content.Length);
					int length = Math.Min(count, Content.Length - from);
					if (CutFrom is { } cut && start >= cut)
					{
						length /= 2;
					}

					string stamp = WithVersions ? $"2049 131 {Content.Length} {1_760_000_000 + version}" : "";
					_ = Interlocked.Increment(ref _answered);
					return new FilePiece(stamp, Content.AsMemory(from, length));
				}
			}
			finally
			{
				_ = Interlocked.Decrement(ref _running);
				slot?.Dispose();
			}
		}

		private static void InterlockedMax(ref int target, int value)
		{
			int seen = Volatile.Read(ref target);
			while (value > seen)
			{
				int previous = Interlocked.CompareExchange(ref target, value, seen);
				if (previous == seen)
				{
					return;
				}

				seen = previous;
			}
		}
	}
}
