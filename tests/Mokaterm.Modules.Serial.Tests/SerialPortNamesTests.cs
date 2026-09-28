using Mokaterm.Modules.Serial.Protocol;

namespace Mokaterm.Modules.Serial.Tests;

public sealed class SerialPortNamesTests
{
	[Theory]
	[InlineData(@"\\.\COM12", "COM12")]
	[InlineData("  COM3  ", "COM3")]
	[InlineData("/dev/ttyUSB0", "/dev/ttyUSB0")]
	[InlineData(null, "")]
	[InlineData("   ", "")]
	public void Normalize_DropsTheDevicePrefixDotNetAddsItself(string? name, string expected) =>
		Assert.Equal(expected, SerialPortNames.Normalize(name));

	[Theory]
	[InlineData("COM1")]
	[InlineData("com9")]
	[InlineData("COM256")]
	[InlineData(@"\\.\COM12")]
	public void Validate_OnWindows_AcceptsComNames(string name) =>
		Assert.Null(SerialPortNames.Validate(name, windows: true));

	[Theory]
	[InlineData("/dev/ttyUSB0")]
	[InlineData("CNCA0")]
	[InlineData("COM")]
	[InlineData("COMx")]
	public void Validate_OnWindows_RefusesWhatTheDriverWouldRefuse(string name) =>
		Assert.NotNull(SerialPortNames.Validate(name, windows: true));

	[Theory]
	[InlineData("/dev/ttyUSB0")]
	[InlineData("/dev/cu.usbserial-1420")]
	[InlineData("COM3")]
	public void Validate_ElsewhereTakesAnyDevicePath(string name) =>
		Assert.Null(SerialPortNames.Validate(name, windows: false));

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("/dev/tty USB0")]
	[InlineData("/dev/tty\tUSB0")]
	public void Validate_RefusesEmptyNamesAndWhitespace(string? name) =>
		Assert.NotNull(SerialPortNames.Validate(name, windows: false));

	[Fact]
	public void Validate_RefusesAControlCharacter() =>
		Assert.NotNull(SerialPortNames.Validate("/dev/tty\u0007USB0", windows: false));

	[Fact]
	public void Validate_TooLong_IsRefused() =>
		Assert.NotNull(SerialPortNames.Validate("/dev/" + new string('x', SerialPortNames.MaxLength), windows: false));

	[Fact]
	public void IsValid_FollowsValidate() =>
		Assert.Equal(SerialPortNames.Validate("COM3") is null, SerialPortNames.IsValid("COM3"));

	[Fact]
	public void List_ReadsWhateverThisMachineHasWithoutOpeningAnything()
	{
		// Names only: this never opens a port, so it is safe on a machine with a device on the other end.
		IReadOnlyList<string> ports = SerialPortNames.List();

		Assert.All(ports, port => Assert.False(string.IsNullOrWhiteSpace(port)));
		Assert.Equal(ports.Distinct(StringComparer.OrdinalIgnoreCase).Count(), ports.Count);
	}
}
