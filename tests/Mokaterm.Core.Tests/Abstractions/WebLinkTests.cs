using Mokaterm.Abstractions.Platform;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>
/// What may leave the app for the user's browser. Any other scheme reaches whatever program registered it, which is why
/// the desktop host's link policy and the URL opener both ask this one rule.
/// </summary>
public sealed class WebLinkTests
{
	[Theory]
	[InlineData("http://localhost:8080/", "http://localhost:8080/")]
	[InlineData("https://grafana.example.com/d/abc?from=now-1h", "https://grafana.example.com/d/abc?from=now-1h")]
	[InlineData("  https://example.com  ", "https://example.com/")]
	[InlineData("HTTPS://EXAMPLE.COM/Path", "https://example.com/Path")]
	[InlineData("http://[::1]:3000/", "http://[::1]:3000/")]
	public void WebAddresses_AreLinks(string address, string expected)
	{
		Assert.True(WebLink.TryCreate(address, out Uri? link));
		Assert.Equal(expected, link.AbsoluteUri);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("example.com")]
	[InlineData("/srv/shop/compose.yaml")]
	[InlineData("file:///C:/Windows/System32/calc.exe")]
	[InlineData("javascript:alert(1)")]
	[InlineData("ms-settings:privacy")]
	[InlineData("ssh://root@example.com")]
	[InlineData("ftp://example.com/")]
	[InlineData("data:text/html,hello")]
	public void EverythingElse_IsRefused(string? address)
	{
		Assert.False(WebLink.TryCreate(address, out Uri? link));
		Assert.Null(link);
	}

	[Fact]
	public void ARelativeUri_IsNotALink() => Assert.False(WebLink.IsWebLink(new Uri("/index.html", UriKind.Relative)));

	[Fact]
	public void NoUri_IsNotALink() => Assert.False(WebLink.IsWebLink(null));
}
