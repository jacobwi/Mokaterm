using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Serial.Protocol;

namespace Mokaterm.Modules.Serial.Tests;

public sealed class SerialConnectionOptionsTests
{
	[Fact]
	public void Default_Is115200_8N1_WithBothPinsAssertedAndNoEcho()
	{
		SerialConnectionOptions options = SerialConnectionOptions.Default;

		Assert.Null(options.PortName);
		Assert.Equal(115200, options.Line.BaudRate);
		Assert.Equal(8, options.Line.DataBits);
		Assert.Equal(SerialParity.None, options.Line.Parity);
		Assert.Equal(SerialStopBits.One, options.Line.StopBits);
		Assert.Equal(SerialFlowControl.None, options.Line.FlowControl);
		Assert.True(options.Dtr);
		Assert.True(options.Rts);
		Assert.Equal(SerialEncodings.Default, options.EncodingName);
		Assert.Equal(SerialLineEnding.Cr, options.LineEnding);
		Assert.False(options.LocalEcho);
	}

	[Fact]
	public void ApplyTo_ThenFrom_KeepsEveryValue()
	{
		SerialConnectionOptions options = new()
		{
			PortName = "COM7",
			Line = new SerialLineSettings
			{
				BaudRate = 9600,
				DataBits = 7,
				Parity = SerialParity.Even,
				StopBits = SerialStopBits.Two,
				FlowControl = SerialFlowControl.RtsCts,
			},
			Dtr = false,
			Rts = false,
			EncodingName = "ibm437",
			LineEnding = SerialLineEnding.CrLf,
			LocalEcho = true,
		};

		SerialConnectionOptions read = SerialConnectionOptions.From(options.ApplyTo(ProtocolOptions.Empty));

		Assert.Equal(options, read);
	}

	[Fact]
	public void ApplyTo_Defaults_ThenFrom_KeepsEveryValue()
	{
		ProtocolOptions written = SerialConnectionOptions.Default.ApplyTo(ProtocolOptions.Empty);

		Assert.Equal(SerialConnectionOptions.Default, SerialConnectionOptions.From(written));

		// The port key is the one value that is left out: empty means the host's address.
		Assert.False(written.ContainsKey(SerialConnectionOptions.PortKey));
	}

	[Fact]
	public void ApplyTo_KeepsKeysThatBelongToSomethingElse()
	{
		ProtocolOptions other = ProtocolOptions.Empty.With("ssh.startupCommand", "tmux");

		ProtocolOptions written = SerialConnectionOptions.Default.ApplyTo(other);

		Assert.Equal("tmux", written.GetString("ssh.startupCommand"));
	}

	[Fact]
	public void From_UnknownEnumValuesAndBlankEncoding_FallBackToTheDefaults()
	{
		ProtocolOptions stored = ProtocolOptions.Empty
			.With(SerialConnectionOptions.ParityKey, "7")
			.With(SerialConnectionOptions.StopBitsKey, "Whatever")
			.With(SerialConnectionOptions.FlowControlKey, "42")
			.With(SerialConnectionOptions.LineEndingKey, "Nonsense")
			.With(SerialConnectionOptions.EncodingKey, "   ");

		SerialConnectionOptions options = SerialConnectionOptions.From(stored);

		Assert.Equal(SerialParity.None, options.Line.Parity);
		Assert.Equal(SerialStopBits.One, options.Line.StopBits);
		Assert.Equal(SerialFlowControl.None, options.Line.FlowControl);
		Assert.Equal(SerialLineEnding.Cr, options.LineEnding);
		Assert.Equal(SerialEncodings.Default, options.EncodingName);
	}

	[Fact]
	public void From_WindowsDevicePath_KeepsTheNameTheDriverWants()
	{
		ProtocolOptions stored = ProtocolOptions.Empty.With(SerialConnectionOptions.PortKey, @"  \\.\COM12  ");

		Assert.Equal("COM12", SerialConnectionOptions.From(stored).PortName);
	}

	[Fact]
	public void HasAny_IsFalseForAConnectionThatWasNeverEdited()
	{
		Assert.False(SerialConnectionOptions.HasAny(ProtocolOptions.Empty));
		Assert.False(SerialConnectionOptions.HasAny(ProtocolOptions.Empty.With("ssh.proxy.kind", "Http")));
		Assert.True(SerialConnectionOptions.HasAny(SerialConnectionOptions.Default.ApplyTo(ProtocolOptions.Empty)));
	}

	[Fact]
	public void ResolvePortName_TakesTheHostAddressUnlessTheConnectionNamesAPort()
	{
		Assert.Equal("COM3", SerialConnectionOptions.Default.ResolvePortName("COM3"));
		Assert.Equal("/dev/ttyUSB0", SerialConnectionOptions.Default.ResolvePortName(" /dev/ttyUSB0 "));
		Assert.Equal("COM9", (SerialConnectionOptions.Default with { PortName = "COM9" }).ResolvePortName("COM3"));
	}

	[Fact]
	public void Validate_ReportsABadPortNameAndABadFrame()
	{
		Assert.Null(SerialConnectionOptions.Default.Validate("COM3"));
		Assert.NotNull(SerialConnectionOptions.Default.Validate(""));

		SerialConnectionOptions impossible = SerialConnectionOptions.Default with
		{
			Line = SerialLineSettings.Default with { StopBits = SerialStopBits.OnePointFive },
		};

		Assert.NotNull(impossible.Validate("COM3"));
	}
}
