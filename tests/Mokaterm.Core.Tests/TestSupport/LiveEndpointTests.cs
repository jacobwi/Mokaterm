namespace Mokaterm.Core.Tests.TestSupport;

/// <summary>
/// The shared parser every live protocol test reads its environment variable through. Those tests are skipped unless
/// the variable is set, so this is what keeps the parsing honest.
/// </summary>
public sealed class LiveEndpointTests
{
	[Theory]
	[InlineData("web01", "web01", 22, false)]
	[InlineData("web01:2222", "web01", 2222, true)]
	[InlineData("user:pw@web01", "web01", 22, false)]
	[InlineData("user:pw@web01:2222", "web01", 2222, true)]
	[InlineData("user:pw@[::1]:2222", "::1", 2222, true)]
	[InlineData("user:pw@[fe80::1%eth0]", "fe80::1%eth0", 22, false)]
	[InlineData("::1", "::1", 22, false)]
	public void Parse_ReadsTheHostAndThePort(string value, string host, int port, bool named)
	{
		LiveEndpoint endpoint = LiveEndpoint.Parse(value, 22);

		Assert.Equal(host, endpoint.Host);
		Assert.Equal(port, endpoint.Port);
		Assert.Equal(named, endpoint.PortWasNamed);
	}

	[Theory]
	[InlineData("web01", null, null)]
	[InlineData("user@web01", "user", null)]
	[InlineData("user:pw@web01", "user", "pw")]
	[InlineData("user:p%40ss%3Aword@web01", "user", "p@ss:word")]
	[InlineData("pw@web01", "pw", null)]
	public void Parse_ReadsTheLoginAndDecodesIt(string value, string? user, string? password)
	{
		LiveEndpoint endpoint = LiveEndpoint.Parse(value, 22);

		Assert.Equal(user, endpoint.User);
		Assert.Equal(password, endpoint.Password);
	}

	[Theory]
	[InlineData("")]
	[InlineData("user:pw@")]
	[InlineData("web01:")]
	[InlineData("web01:https")]
	[InlineData("web01:70000")]
	[InlineData("[::1")]
	[InlineData("[::1]22")]
	public void Parse_RefusesAValueThatWouldOtherwiseBecomeAHostNobodyNamed(string value) =>
		Assert.Throws<FormatException>(() => LiveEndpoint.Parse(value, 22));

	[Fact]
	public void Query_IsReadByName_AndDoesNotReachTheHost()
	{
		LiveEndpoint endpoint = LiveEndpoint.Parse("user:pw@web01:990?mode=OrdinalIgnoreCase&tls&path=%2Fmqtt", 21);

		Assert.Equal("web01", endpoint.Host);
		Assert.Equal(990, endpoint.Port);
		Assert.Equal("/mqtt", endpoint.Option("path"));
		Assert.Null(endpoint.Option("domain"));
		Assert.True(endpoint.Flag("tls"));
		Assert.False(endpoint.Flag("keepalive"));
		Assert.Equal(StringComparison.OrdinalIgnoreCase, endpoint.Option("mode", StringComparison.Ordinal));
		Assert.Equal(StringComparison.Ordinal, endpoint.Option("missing", StringComparison.Ordinal));
	}

	[Fact]
	public void Query_AValueThatIsNotAMemberOfTheEnum_IsRefusedRatherThanIgnored()
	{
		LiveEndpoint endpoint = LiveEndpoint.Parse("web01?mode=sideways", 22);

		Assert.ThrowsAny<ArgumentException>(() => endpoint.Option("mode", StringComparison.Ordinal));
	}

	[Fact]
	public void RequireLogin_RefusesAValueWithoutOne() =>
		Assert.Throws<FormatException>(() => LiveEndpoint.Parse("web01", 22).RequireLogin("MOKATERM_TEST_X"));
}
