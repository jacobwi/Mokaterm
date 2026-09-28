using Mokaterm.Modules.Serial.Protocol;

namespace Mokaterm.Modules.Serial.Tests;

public sealed class SerialLineSettingsTests
{
	[Fact]
	public void Frame_IsWrittenTheWayADataSheetWritesIt()
	{
		Assert.Equal("115200 8N1", SerialLineSettings.Default.Frame);

		SerialLineSettings odd = new()
		{
			BaudRate = 9600,
			DataBits = 7,
			Parity = SerialParity.Odd,
			StopBits = SerialStopBits.Two,
		};

		Assert.Equal("9600 7O2", odd.Frame);
	}

	[Theory]
	[InlineData(SerialFlowControl.None, "115200 8N1, no flow control")]
	[InlineData(SerialFlowControl.XOnXOff, "115200 8N1, XON/XOFF")]
	[InlineData(SerialFlowControl.RtsCts, "115200 8N1, RTS/CTS")]
	[InlineData(SerialFlowControl.RtsCtsXOnXOff, "115200 8N1, RTS/CTS and XON/XOFF")]
	public void Describe_NamesTheLineAndItsFlowControl(SerialFlowControl flowControl, string expected) =>
		Assert.Equal(expected, (SerialLineSettings.Default with { FlowControl = flowControl }).Describe());

	[Fact]
	public void Validate_AcceptsTheUsualLines()
	{
		Assert.Null(SerialLineSettings.Default.Validate());
		Assert.Null((SerialLineSettings.Default with { BaudRate = 9600, DataBits = 7, Parity = SerialParity.Even }).Validate());

		// Five data bits go with one and a half stop bits, which is the one place that value is legal.
		Assert.Null((SerialLineSettings.Default with { DataBits = 5, StopBits = SerialStopBits.OnePointFive }).Validate());
	}

	[Theory]
	[InlineData(0)]
	[InlineData(49)]
	[InlineData(4_000_001)]
	public void Validate_RefusesABaudRateOutsideTheRange(int baudRate) =>
		Assert.NotNull((SerialLineSettings.Default with { BaudRate = baudRate }).Validate());

	[Theory]
	[InlineData(4)]
	[InlineData(9)]
	public void Validate_RefusesADataBitCountNoUartHas(int dataBits) =>
		Assert.NotNull((SerialLineSettings.Default with { DataBits = dataBits }).Validate());

	[Fact]
	public void Validate_RefusesTheTwoFramesAUartCannotDo()
	{
		Assert.NotNull((SerialLineSettings.Default with { DataBits = 8, StopBits = SerialStopBits.OnePointFive }).Validate());
		Assert.NotNull((SerialLineSettings.Default with { DataBits = 5, StopBits = SerialStopBits.Two }).Validate());
	}

	[Fact]
	public void CanSetRts_IsFalseOnlyWhenHardwareFlowControlDrivesThePin()
	{
		Assert.True(SerialLineSettings.Default.CanSetRts);
		Assert.True((SerialLineSettings.Default with { FlowControl = SerialFlowControl.XOnXOff }).CanSetRts);
		Assert.False((SerialLineSettings.Default with { FlowControl = SerialFlowControl.RtsCts }).CanSetRts);
		Assert.False((SerialLineSettings.Default with { FlowControl = SerialFlowControl.RtsCtsXOnXOff }).CanSetRts);
	}

	[Fact]
	public void CommonBaudRates_AreOfferedInOrderAndInclude115200()
	{
		Assert.Contains(115200, SerialLineSettings.CommonBaudRates);
		Assert.Equal(SerialLineSettings.CommonBaudRates.Order(), SerialLineSettings.CommonBaudRates);
	}
}
