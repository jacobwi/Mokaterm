using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>
/// Which way a connection signs in, which is what decides the config: CredSSP on or off, INFO_AUTOLOGON on or off, and
/// whether the password goes into it at all. <see cref="RdpSignInTests"/> reads what each route puts on the wire.
/// </summary>
public sealed class RdpSignInRouteTests
{
	private static readonly RdpConnectionOptions NlaOff = RdpConnectionOptions.Default with { NetworkLevelAuthentication = false };

	[Fact]
	public void NlaOn_WithAPassword_ChecksTheLoginWithCredssp()
	{
		using LoginCredentials login = Login("s3cret");

		Assert.Equal(RdpSignInRoute.NetworkLevelAuthentication, RdpConnector.RouteFor(RdpConnectionOptions.Default, login));
	}

	// The autologon switch only exists while network level authentication is off, so a stored "off" changes nothing here.
	[Fact]
	public void NlaOn_IgnoresTheAutologonSwitch()
	{
		using LoginCredentials login = Login("s3cret");

		Assert.Equal(
			RdpSignInRoute.NetworkLevelAuthentication,
			RdpConnector.RouteFor(RdpConnectionOptions.Default with { Autologon = false }, login));
	}

	[Fact]
	public void NlaOff_WithAPassword_SignsInThroughTheClientInfo()
	{
		using LoginCredentials login = Login("s3cret");

		Assert.Equal(RdpSignInRoute.Automatic, RdpConnector.RouteFor(NlaOff, login));
	}

	[Fact]
	public void NlaOff_WithAutologonOff_LeavesTheSignInScreenUp()
	{
		using LoginCredentials login = Login("s3cret");

		Assert.Equal(RdpSignInRoute.SignInScreen, RdpConnector.RouteFor(NlaOff with { Autologon = false }, login));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void NlaOff_WithNoPassword_LeavesTheSignInScreenUp(string? password)
	{
		using LoginCredentials login = Login(password);

		Assert.Equal(RdpSignInRoute.SignInScreen, RdpConnector.RouteFor(NlaOff, login));
	}

	[Fact]
	public void AnAnonymousLogin_LeavesTheSignInScreenUpWhateverTheOptions()
	{
		using LoginCredentials anonymous = new() { Username = "", Method = AuthenticationMethod.Anonymous };

		Assert.Equal(RdpSignInRoute.SignInScreen, RdpConnector.RouteFor(RdpConnectionOptions.Default, anonymous));
		Assert.Equal(RdpSignInRoute.SignInScreen, RdpConnector.RouteFor(NlaOff, anonymous));
		Assert.Equal(RdpSignInRoute.SignInScreen, RdpConnector.RouteFor(RdpConnectionOptions.Default, null));
	}

	private static LoginCredentials Login(string? password) => new()
	{
		Username = "alice",
		Method = AuthenticationMethod.Password,
		Password = password is null ? null : SecretBuffer.FromString(password),
	};
}
