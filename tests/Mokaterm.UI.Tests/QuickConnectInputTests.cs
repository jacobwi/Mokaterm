using Mokaterm.Abstractions.Connections;
using Mokaterm.UI.QuickConnect;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// How the quick connect box reads its text: the protocol a scheme names wins over the picker, a protocol with a port
/// reads <c>user@host:port</c>, one without takes the whole text and lets its module judge it, and anything that does
/// not read says what that protocol expects.
/// </summary>
public sealed class QuickConnectInputTests
{
	private static readonly QuickConnectProtocols Protocols = new();

	[Theory]
	[InlineData("root@web-1:2222", "ssh", "root", "web-1", 2222)]
	[InlineData("web-1", "ssh", null, "web-1", null)]
	[InlineData("vnc://10.0.0.5:5901", "vnc", null, "10.0.0.5", 5901)]
	[InlineData("RDP://admin@desk", "rdp", "admin", "desk", null)]
	[InlineData("ssh://root@[::1]:22", "ssh", "root", "::1", 22)]
	public void Resolve_HostProtocols_ReadUserHostAndPort(string text, string protocol, string? user, string address, int? port)
	{
		QuickConnectResolution resolution = QuickConnectInput.Resolve(text, "ssh", Protocols);

		Assert.True(resolution.IsValid, resolution.Problem);
		Assert.Equal(protocol, resolution.Protocol.Id);
		Assert.Equal(user, resolution.Target.Username);
		Assert.Equal(address, resolution.Target.Address);
		Assert.Equal(port, resolution.Target.Port);
	}

	// The gap this closes: a device path is not a host, so with the host rule it could never be reached.
	[Theory]
	[InlineData("serial:///dev/ttyUSB0", "ssh", "/dev/ttyUSB0")]
	[InlineData("/dev/ttyUSB0", "serial", "/dev/ttyUSB0")]
	[InlineData("/dev/serial/by-id/usb-FTDI_FT232R-if00:1", "serial", "/dev/serial/by-id/usb-FTDI_FT232R-if00:1")]
	[InlineData("COM3", "serial", "COM3")]
	[InlineData("local://Ubuntu-22.04", "ssh", "Ubuntu-22.04")]
	[InlineData("web_1", "docker", "web_1")]
	public void Resolve_ProtocolsWithoutAPort_TakeTheWholeTextAsTheAddress(string text, string picked, string address)
	{
		QuickConnectResolution resolution = QuickConnectInput.Resolve(text, picked, Protocols);

		Assert.True(resolution.IsValid, resolution.Problem);
		Assert.Equal(address, resolution.Target.Address);
		Assert.Null(resolution.Target.Username);
		Assert.Null(resolution.Target.Port);
	}

	[Fact]
	public void Resolve_SerialName_GoesThroughTheModulesOwnCheck()
	{
		QuickConnectResolution resolution = QuickConnectInput.Resolve("ttyS0", "serial", Protocols);

		Assert.False(resolution.IsValid);
		Assert.Equal("On Windows a port name is COM followed by its number, for example COM3.", resolution.Problem);
		Assert.Equal("serial", resolution.Protocol?.Id);
	}

	[Theory]
	[InlineData("a b", "ssh", "For SSH, type user@host:port.")]
	[InlineData("host/path", "ssh", "For SSH, type user@host:port.")]
	[InlineData("@desk", "rdp", "For RDP, type user@host.")]
	[InlineData("vnc://", "ssh", "For VNC, type host:5900.")]
	[InlineData("COM 3", "serial", "For Serial, type COM3.")]
	[InlineData("my container", "docker", "For Docker container, type container name.")]
	public void Resolve_TextThatDoesNotRead_SaysWhatThatProtocolExpects(string text, string picked, string problem)
	{
		QuickConnectResolution resolution = QuickConnectInput.Resolve(text, picked, Protocols);

		Assert.False(resolution.IsValid);
		Assert.Equal(problem, resolution.Problem);
	}

	[Theory]
	[InlineData("host:0")]
	[InlineData("root@host:65536")]
	[InlineData("ssh://[::1]:99999")]
	public void Resolve_PortOutOfRange_SaysSo(string text) =>
		Assert.Equal(QuickConnectInput.PortRange, QuickConnectInput.Resolve(text, "ssh", Protocols).Problem);

	[Fact]
	public void Resolve_SchemeNoModuleRegistered_NamesIt()
	{
		QuickConnectResolution resolution = QuickConnectInput.Resolve("gopher://host", "ssh", Protocols);

		Assert.False(resolution.IsValid);
		Assert.Equal("No installed module opens gopher:// addresses.", resolution.Problem);
	}

	[Theory]
	[InlineData("rdp://", "ssh", "rdp")]
	[InlineData("SERIAL:///dev/tty", "ssh", "serial")]
	[InlineData("root@host", "vnc", "vnc")]
	[InlineData("gopher://host", "vnc", "vnc")]
	public void ProtocolFor_FollowsARegisteredScheme(string text, string picked, string expected) =>
		Assert.Equal(expected, QuickConnectInput.ProtocolFor(text, picked, Protocols));

	[Fact]
	public void Example_FallsBackForAProtocolThatNamesNone()
	{
		Assert.Equal("COM3", QuickConnectInput.Example(QuickConnectProtocols.Serial));
		Assert.Equal(QuickConnectInput.DefaultExample, QuickConnectInput.Example(QuickConnectProtocols.Ssh with { QuickConnectExample = null }));
		Assert.Equal(QuickConnectInput.DefaultExample, QuickConnectInput.Example(null));
	}

	[Fact]
	public void CreateProfiles_FillsAnUnsavedHostAndLogin()
	{
		DateTimeOffset now = DateTimeOffset.Parse("2026-10-11T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

		(HostProfile host, ConnectionProfile login) = QuickConnectInput.CreateProfiles(
			QuickConnectProtocols.Vnc,
			new QuickConnectTarget("vnc", "", "10.0.0.5", 5901),
			now);

		Assert.Equal("10.0.0.5", host.Address);
		Assert.Equal(host.Id, login.HostId);
		Assert.Equal("vnc", login.ProtocolId);
		Assert.Equal(5901, login.Port);
		Assert.Null(login.Username);
		Assert.Equal(AuthenticationMethod.Password, login.AuthenticationMethod);
		Assert.Equal(now, login.CreatedAt);
	}

	// A serial line has no port, so a number that reached the target anyway must not reach the login.
	[Fact]
	public void CreateProfiles_ProtocolWithoutAPort_DropsThePort()
	{
		(_, ConnectionProfile login) = QuickConnectInput.CreateProfiles(
			QuickConnectProtocols.Serial,
			new QuickConnectTarget("serial", null, "COM3", 22),
			DateTimeOffset.UnixEpoch);

		Assert.Null(login.Port);
		Assert.Equal(AuthenticationMethod.Anonymous, login.AuthenticationMethod);
	}
}
