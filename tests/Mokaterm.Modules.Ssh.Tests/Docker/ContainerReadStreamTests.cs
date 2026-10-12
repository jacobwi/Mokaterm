using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>A file read a piece at a time: pieces grow to the cap, a short one ends the file, and nothing is held whole.</summary>
public sealed class ContainerReadStreamTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Pieces_GrowToTheCap_AndAShortOneIsTheEnd()
	{
		byte[] file = Bytes((10 * 1024 * 1024) + 7);
		List<(long Start, int Count)> asked = [];

		await using ContainerReadStream stream = await ContainerReadStream.OpenAsync(Reader(file, asked), 0, Ct);
		using MemoryStream read = new();
		await stream.CopyToAsync(read, Ct);

		Assert.Equal(file, read.ToArray());
		Assert.Equal(
			[
				(0L, ContainerReadStream.FirstPieceBytes),
				(256L * 1024, 1024 * 1024),
				(1280L * 1024, ContainerReadStream.MaxPieceBytes),
				(5376L * 1024, ContainerReadStream.MaxPieceBytes),
				(9472L * 1024, ContainerReadStream.MaxPieceBytes),
			],
			asked);
	}

	// A file of exactly one piece cannot tell its end from the piece; the next piece comes back empty.
	[Fact]
	public async Task AFileOfExactlyOnePiece_EndsAtAnEmptyPiece()
	{
		byte[] file = Bytes(ContainerReadStream.FirstPieceBytes);
		List<(long Start, int Count)> asked = [];

		await using ContainerReadStream stream = await ContainerReadStream.OpenAsync(Reader(file, asked), 0, Ct);
		using MemoryStream read = new();
		await stream.CopyToAsync(read, Ct);

		Assert.Equal(file, read.ToArray());
		Assert.Equal(2, asked.Count);
		Assert.Equal(0, await stream.ReadAsync(new byte[16], Ct));
		Assert.Equal(2, asked.Count);
	}

	[Fact]
	public async Task AStartPastZero_IsWhereTheFirstPieceBegins()
	{
		byte[] file = Bytes(1000);
		List<(long Start, int Count)> asked = [];

		await using ContainerReadStream stream = await ContainerReadStream.OpenAsync(Reader(file, asked), 600, Ct);
		using MemoryStream read = new();
		await stream.CopyToAsync(read, Ct);

		Assert.Equal(file.AsSpan(600).ToArray(), read.ToArray());
		Assert.Equal(600, asked[0].Start);
	}

	[Fact]
	public async Task AFailure_OfTheFirstPiece_IsTheOpensFailure()
	{
		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(() => ContainerReadStream.OpenAsync(
			(_, _, _) => Task.FromException<ReadOnlyMemory<byte>>(new RemoteFileSystemException(RemoteFileErrorKind.NotFound, "/nope does not exist.", "/nope")),
			0,
			Ct));

		Assert.Equal(RemoteFileErrorKind.NotFound, failure.Kind);
	}

	[Fact]
	public async Task ASynchronousRead_WorksToo()
	{
		byte[] file = Bytes(300 * 1024);
		await using ContainerReadStream stream = await ContainerReadStream.OpenAsync(Reader(file, []), 0, Ct);
		using MemoryStream read = new();

		stream.CopyTo(read);

		Assert.Equal(file, read.ToArray());
	}

	private static Func<long, int, CancellationToken, Task<ReadOnlyMemory<byte>>> Reader(byte[] file, List<(long Start, int Count)> asked) =>
		(start, count, _) =>
		{
			asked.Add((start, count));
			int from = (int)Math.Min(start, file.Length);
			return Task.FromResult<ReadOnlyMemory<byte>>(file.AsMemory(from, Math.Min(count, file.Length - from)));
		};

	private static byte[] Bytes(int length)
	{
		byte[] bytes = new byte[length];
		for (int index = 0; index < length; index++)
		{
			bytes[index] = (byte)(index * 31 % 251);
		}

		return bytes;
	}
}
