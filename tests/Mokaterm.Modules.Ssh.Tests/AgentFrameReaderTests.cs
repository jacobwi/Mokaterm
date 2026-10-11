using System.Buffers.Binary;
using Mokaterm.Modules.Ssh.Forwarding;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class AgentFrameReaderTests
{
	[Fact]
	public void Frame_PutsTheLengthFirst()
	{
		Assert.Equal([0, 0, 0, 3, 11, 12, 13], AgentFrameReader.Frame([11, 12, 13]));
	}

	[Fact]
	public void TryTake_ReassemblesAMessageSplitAnywhere()
	{
		byte[] message = [.. Enumerable.Range(0, 300).Select(value => (byte)value)];
		byte[] frame = AgentFrameReader.Frame(message);

		for (int split = 1; split < frame.Length; split += 37)
		{
			AgentFrameReader reader = new();
			Assert.True(reader.Append(frame.AsSpan(0, split)));
			Assert.False(reader.TryTake(out _));
			Assert.True(reader.Append(frame.AsSpan(split)));

			Assert.True(reader.TryTake(out byte[]? taken));
			Assert.Equal(message, taken);
			Assert.False(reader.TryTake(out _));
		}
	}

	[Fact]
	public void TryTake_SeparatesMessagesThatArrivedTogether()
	{
		AgentFrameReader reader = new();

		Assert.True(reader.Append([.. AgentFrameReader.Frame([11]), .. AgentFrameReader.Frame([13, 1, 2]), 0, 0]));

		Assert.True(reader.TryTake(out byte[]? first));
		Assert.Equal([11], first);
		Assert.True(reader.TryTake(out byte[]? second));
		Assert.Equal([13, 1, 2], second);
		Assert.False(reader.TryTake(out _));
	}

	[Fact]
	public void Append_TakesTheLongestMessageOpenSshAllows()
	{
		AgentFrameReader reader = new();
		byte[] message = new byte[AgentFrameReader.MaxMessageBytes];

		Assert.True(reader.Append(AgentFrameReader.Frame(message)));
		Assert.True(reader.TryTake(out byte[]? taken));
		Assert.Equal(message.Length, taken.Length);
	}

	[Theory]
	[InlineData(0u)]
	[InlineData((uint)AgentFrameReader.MaxMessageBytes + 1)]
	[InlineData(uint.MaxValue)]
	public void Append_BreaksOnALengthOutsideTheProtocol(uint length)
	{
		AgentFrameReader reader = new();
		byte[] header = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(header, length);

		Assert.False(reader.Append(header));

		Assert.True(reader.IsBroken);
		Assert.False(reader.TryTake(out _));
		Assert.False(reader.Append(AgentFrameReader.Frame([11])));
	}

	[Fact]
	public void TryTake_GivesTheGoodMessageBeforeABadOne()
	{
		AgentFrameReader reader = new();

		Assert.True(reader.Append([.. AgentFrameReader.Frame([11]), 0, 0, 0, 0]));

		Assert.True(reader.TryTake(out byte[]? first));
		Assert.Equal([11], first);
		Assert.True(reader.IsBroken);
	}
}
