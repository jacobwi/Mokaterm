using Mokaterm.Abstractions.Connections;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

// The window title is seen outside the app (taskbar, Alt+Tab, screen shares), so it says only what the tab says.
public sealed class WindowTitleTextTests
{
	private static readonly HostProfile Machine = new() { Id = Guid.NewGuid(), Address = "10.0.0.5" };

	[Fact]
	public void NoSession_IsNoSubject()
	{
		Assert.Null(WindowTitleText.Subject(null));
	}

	[Fact]
	public void ASession_IsItsUserAtItsMachine()
	{
		Assert.Equal("bc@build-01.example.com", WindowTitleText.Subject(new FakeSession()));
	}

	[Fact]
	public void ALoginWithALabel_PutsTheLabelFirst()
	{
		ConnectionProfile login = Login("root") with { Label = "  prod db  " };

		Assert.Equal("prod db (root@10.0.0.5)", WindowTitleText.Subject(Machine, login));
	}

	[Fact]
	public void AMachineWithAName_PutsTheNameFirst_UnlessItIsTheAddress()
	{
		Assert.Equal("web01 (root@10.0.0.5)", WindowTitleText.Subject(Machine with { Name = "web01" }, Login("root")));
		Assert.Equal("root@10.0.0.5", WindowTitleText.Subject(Machine with { Name = "10.0.0.5" }, Login("root")));
	}

	[Fact]
	public void ALoginWithoutAUser_IsTheAddressAlone()
	{
		Assert.Equal("10.0.0.5", WindowTitleText.Subject(Machine, Login(null)));
	}

	private static ConnectionProfile Login(string? user) => new()
	{
		Id = Guid.NewGuid(),
		HostId = Machine.Id,
		ProtocolId = "ssh",
		Username = user,
	};
}
