using Mokaterm.Modules.Serial.Protocol;

namespace Mokaterm.Modules.Serial.Tests;

public sealed class SerialSettingsTests
{
	[Fact]
	public void SectionKey_IsSerial() => Assert.Equal("serial", SerialSettings.SectionKey);

	[Fact]
	public void Defaults_AreUsable()
	{
		SerialSettings settings = new();

		Assert.Equal(settings, settings.Clamped());
		Assert.Equal("115200 8N1, no flow control", settings.Line.Describe());
		Assert.Null(settings.Line.Validate());
		Assert.True(settings.Dtr);
		Assert.True(settings.Rts);
		Assert.False(settings.LocalEcho);
		Assert.Equal(SerialLineEnding.Cr, settings.LineEnding);

		// A read waits forever by default: a device that says nothing for an hour is quiet, not broken.
		Assert.Equal(0, settings.ReadTimeoutMilliseconds);
		Assert.Equal(TimeSpan.Zero, settings.ReadTimeout);
	}

	[Fact]
	public void NewConnectionOptions_AreWhatANewConnectionStartsFrom()
	{
		SerialSettings settings = new()
		{
			BaudRate = 9600,
			DataBits = 7,
			Parity = SerialParity.Even,
			FlowControl = SerialFlowControl.XOnXOff,
			Dtr = false,
			Rts = false,
			LineEnding = SerialLineEnding.CrLf,
			LocalEcho = true,
			EncodingName = "ibm437",
		};

		SerialConnectionOptions options = settings.NewConnectionOptions;

		Assert.Equal(settings.Line, options.Line);
		Assert.False(options.Dtr);
		Assert.False(options.Rts);
		Assert.Equal(SerialLineEnding.CrLf, options.LineEnding);
		Assert.True(options.LocalEcho);
		Assert.Equal("ibm437", options.EncodingName);

		// The port always comes from the connection: a default port name would point every login at one device.
		Assert.Null(options.PortName);
	}

	[Fact]
	public void Clamped_PullsEveryValueIntoRange()
	{
		SerialSettings clamped = new SerialSettings
		{
			BaudRate = 0,
			DataBits = 1,
			WriteTimeoutMilliseconds = 1,
			ReadTimeoutMilliseconds = 5,
			DeviceCheckSeconds = -3,
			BreakMilliseconds = 0,
			EncodingName = "no-such-charset",
		}.Clamped();

		Assert.Equal(SerialLineSettings.MinBaudRate, clamped.BaudRate);
		Assert.Equal(SerialLineSettings.MinDataBits, clamped.DataBits);
		Assert.Equal(SerialSettings.MinWriteTimeoutMilliseconds, clamped.WriteTimeoutMilliseconds);
		Assert.Equal(SerialSettings.MinReadTimeoutMilliseconds, clamped.ReadTimeoutMilliseconds);
		Assert.Equal(0, clamped.DeviceCheckSeconds);
		Assert.Equal(TimeSpan.Zero, clamped.DeviceCheck);
		Assert.Equal(SerialSettings.MinBreakMilliseconds, clamped.BreakMilliseconds);
		Assert.Equal(SerialEncodings.Default, clamped.EncodingName);
	}

	[Fact]
	public void Clamped_HugeValues_StopAtTheMaximum()
	{
		SerialSettings clamped = new SerialSettings
		{
			BaudRate = int.MaxValue,
			DataBits = int.MaxValue,
			WriteTimeoutMilliseconds = int.MaxValue,
			ReadTimeoutMilliseconds = int.MaxValue,
			DeviceCheckSeconds = int.MaxValue,
			BreakMilliseconds = int.MaxValue,
		}.Clamped();

		Assert.Equal(SerialLineSettings.MaxBaudRate, clamped.BaudRate);
		Assert.Equal(SerialLineSettings.MaxDataBits, clamped.DataBits);
		Assert.Equal(SerialSettings.MaxWriteTimeoutMilliseconds, clamped.WriteTimeoutMilliseconds);
		Assert.Equal(SerialSettings.MaxReadTimeoutMilliseconds, clamped.ReadTimeoutMilliseconds);
		Assert.Equal(SerialSettings.MaxDeviceCheckSeconds, clamped.DeviceCheckSeconds);
		Assert.Equal(SerialSettings.MaxBreakMilliseconds, clamped.BreakMilliseconds);
	}

	[Fact]
	public void Clamped_ZeroReadTimeout_StaysZeroSoAReadWaitsForever()
	{
		Assert.Equal(0, new SerialSettings { ReadTimeoutMilliseconds = 0 }.Clamped().ReadTimeoutMilliseconds);
		Assert.Equal(0, new SerialSettings { ReadTimeoutMilliseconds = -9 }.Clamped().ReadTimeoutMilliseconds);
	}

	[Fact]
	public void Clamped_UndefinedEnumValues_FallBackToTheDefaults()
	{
		SerialSettings clamped = new SerialSettings
		{
			Parity = (SerialParity)42,
			StopBits = (SerialStopBits)42,
			FlowControl = (SerialFlowControl)42,
			LineEnding = (SerialLineEnding)42,
		}.Clamped();

		Assert.Equal(SerialParity.None, clamped.Parity);
		Assert.Equal(SerialStopBits.One, clamped.StopBits);
		Assert.Equal(SerialFlowControl.None, clamped.FlowControl);
		Assert.Equal(SerialLineEnding.Cr, clamped.LineEnding);
	}

	[Fact]
	public void Clamped_CorrectsAFrameNoUartCanDo()
	{
		SerialSettings wide = new SerialSettings { DataBits = 8, StopBits = SerialStopBits.OnePointFive }.Clamped();
		SerialSettings narrow = new SerialSettings { DataBits = 5, StopBits = SerialStopBits.Two }.Clamped();

		Assert.Equal(SerialStopBits.One, wide.StopBits);
		Assert.Equal(SerialStopBits.OnePointFive, narrow.StopBits);
		Assert.Null(wide.Line.Validate());
		Assert.Null(narrow.Line.Validate());
	}
}
