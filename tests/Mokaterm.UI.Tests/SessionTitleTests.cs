using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.UI.Presentation;

namespace Mokaterm.UI.Tests;

public sealed class SessionTitleTests
{
	[Fact]
	public void Name_PrefersTheLoginLabel()
	{
		HostProfile host = Host("10.10.2.3", name: "Build server");

		Assert.Equal("Deploy box", SessionTitle.Name(host, Connection(label: "  Deploy box  ")));
	}

	[Fact]
	public void Name_FallsBackToTheMachineName()
	{
		HostProfile host = Host("10.10.2.3", name: "Build server");

		Assert.Equal("Build server", SessionTitle.Name(host, Connection()));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("10.10.2.3")]
	[InlineData("10.10.2.3 ")]
	public void Name_IsNull_WhenItWouldOnlyRepeatTheAddress(string hostName) =>
		Assert.Null(SessionTitle.Name(Host("10.10.2.3", hostName), Connection()));

	[Fact]
	public void Endpoint_IsUserAtHost_AndTheAddressAloneWithoutAUser()
	{
		HostProfile host = Host("10.10.2.3");

		Assert.Equal("abc@10.10.2.3", SessionTitle.Endpoint(host, Connection(username: "abc")));
		Assert.Equal("10.10.2.3", SessionTitle.Endpoint(host, Connection(username: " ")));
	}

	private static HostProfile Host(string address, string name = "") =>
		new() { Id = Guid.NewGuid(), Address = address, Name = name };

	private static ConnectionProfile Connection(string? username = null, string? label = null) => new()
	{
		Id = Guid.NewGuid(),
		HostId = Guid.NewGuid(),
		ProtocolId = "ssh",
		Username = username,
		Label = label,
		AuthenticationMethod = AuthenticationMethod.Password,
	};
}
