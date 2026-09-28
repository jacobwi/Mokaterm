using Mokaterm.Modules.Telnet.Protocol;

namespace Mokaterm.Modules.Telnet.Tests;

public sealed class TelnetInputFilterTests
{
	[Fact]
	public void Filter_CarriageReturnNul_DropsTheNul()
	{
		byte[] data = [(byte)'a', 0x0D, 0x00, (byte)'b'];

		int length = new TelnetInputFilter().Filter(data, binary: false);

		Assert.Equal([(byte)'a', 0x0D, (byte)'b'], data[..length]);
	}

	[Fact]
	public void Filter_CarriageReturnLineFeed_IsLeftAlone()
	{
		byte[] data = [(byte)'a', 0x0D, 0x0A];

		int length = new TelnetInputFilter().Filter(data, binary: false);

		Assert.Equal(data, data[..length]);
	}

	[Fact]
	public void Filter_NulAfterTheCarriageReturnOfAnEarlierChunk_IsStillDropped()
	{
		TelnetInputFilter filter = new();
		byte[] first = [(byte)'a', 0x0D];
		byte[] second = [0x00, (byte)'b'];

		int firstLength = filter.Filter(first, binary: false);
		int secondLength = filter.Filter(second, binary: false);

		Assert.Equal([(byte)'a', 0x0D], first[..firstLength]);
		Assert.Equal([(byte)'b'], second[..secondLength]);
	}

	[Fact]
	public void Filter_Binary_KeepsEveryByte()
	{
		byte[] data = [0x0D, 0x00, 0x00];

		int length = new TelnetInputFilter().Filter(data, binary: true);

		Assert.Equal(3, length);
	}

	[Fact]
	public void Filter_NulWithoutACarriageReturn_IsKept()
	{
		byte[] data = [(byte)'a', 0x00];

		int length = new TelnetInputFilter().Filter(data, binary: false);

		Assert.Equal(2, length);
	}
}
