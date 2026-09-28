using System.Text;
using Mokaterm.Sessions.Terminal;

namespace Mokaterm.Sessions.Tests;

public sealed class ReplayBufferTests
{
	[Fact]
	public void Snapshot_KeepsTheNewestBytesInOrder()
	{
		ReplayBuffer buffer = new(8);

		buffer.Append("abc"u8);
		buffer.Append("defgh"u8);
		buffer.Append("ij"u8);

		Assert.Equal("cdefghij", SnapshotText(buffer));
	}

	[Fact]
	public void Append_LargerThanCapacity_KeepsTheTail()
	{
		ReplayBuffer buffer = new(4);

		buffer.Append("xy"u8);
		buffer.Append("abcdef"u8);

		Assert.Equal("cdef", SnapshotText(buffer));
	}

	[Fact]
	public void Snapshot_AfterTrimmingInsideACharacter_SkipsItsOrphanedBytes()
	{
		ReplayBuffer buffer = new(2);

		// "é" is two bytes; keeping the last two bytes of "éb" leaves only the second byte of "é".
		buffer.Append(Encoding.UTF8.GetBytes("éb"));

		Assert.Equal("b", SnapshotText(buffer));
	}

	[Fact]
	public void Snapshot_WithZeroCapacity_IsEmpty()
	{
		ReplayBuffer buffer = new(0);

		buffer.Append("abc"u8);

		Assert.Null(buffer.Snapshot());
	}

	private static string SnapshotText(ReplayBuffer buffer)
	{
		OutputChunk? snapshot = buffer.Snapshot();
		Assert.NotNull(snapshot);
		try
		{
			return Encoding.UTF8.GetString(snapshot.Value.Memory.Span);
		}
		finally
		{
			snapshot.Value.Return();
		}
	}
}
