using System.Net.Sockets;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>
/// The table five protocol modules used to keep a copy of. The sentences are asserted here and not only the failure
/// kinds, because the copies drifted apart in their wording first and in what they mapped second.
/// </summary>
public sealed class SocketFailuresTests
{
	private const string Endpoint = "web01:5900";

	private static readonly SocketFailureHints Hints = new()
	{
		Refused = "Check the port.",
		Reset = "A server that expects TLS closes a plain connection this way.",
	};

	[Theory]
	[InlineData(SocketError.HostNotFound)]
	[InlineData(SocketError.NoData)]
	[InlineData(SocketError.TryAgain)]
	public async Task DescribeAsync_ANameThatDoesNotResolve_SaysSoWithoutLookingItUp(SocketError error)
	{
		SocketFailureReport report = await DescribeAsync(error);

		Assert.Equal(ConnectFailure.HostUnreachable, report.Failure);
		Assert.Equal("Could not find web01. Check the host name.", report.Message);
	}

	[Fact]
	public async Task DescribeAsync_ATimedOutDial_IsATimeout()
	{
		SocketFailureReport report = await DescribeAsync(SocketError.TimedOut);

		Assert.Equal(ConnectFailure.Timeout, report.Failure);
		Assert.Equal("web01:5900 did not answer in time.", report.Message);
	}

	[Fact]
	public async Task DescribeAsync_ARefusedPort_CarriesTheProtocolsOwnHint()
	{
		SocketFailureReport report = await DescribeAsync(SocketError.ConnectionRefused);

		Assert.Equal(ConnectFailure.HostUnreachable, report.Failure);
		Assert.Equal("web01:5900 refused the connection. Check the port.", report.Message);
	}

	[Theory]
	[InlineData(SocketError.NetworkUnreachable)]
	[InlineData(SocketError.HostUnreachable)]
	public async Task DescribeAsync_NoRoute_SaysThatRatherThanGuessing(SocketError error)
	{
		SocketFailureReport report = await DescribeAsync(error);

		Assert.Equal(ConnectFailure.HostUnreachable, report.Failure);
		Assert.Equal("There is no route to web01:5900.", report.Message);
	}

	[Theory]
	[InlineData(SocketError.ConnectionReset)]
	[InlineData(SocketError.ConnectionAborted)]
	[InlineData(SocketError.Shutdown)]
	public async Task DescribeAsync_AClosedConnection_IsAProtocolError(SocketError error)
	{
		SocketFailureReport report = await DescribeAsync(error);

		Assert.Equal(ConnectFailure.ProtocolError, report.Failure);
		Assert.Equal(
			"web01:5900 closed the connection unexpectedly. A server that expects TLS closes a plain connection this way.",
			report.Message);
	}

	[Fact]
	public async Task DescribeAsync_WithoutHints_LeavesTheSharedSentencesAlone()
	{
		SocketFailureReport refused = await DescribeAsync(SocketError.ConnectionRefused, hints: SocketFailureHints.None);
		SocketFailureReport reset = await DescribeAsync(SocketError.ConnectionReset, hints: SocketFailureHints.None);

		Assert.Equal("web01:5900 refused the connection.", refused.Message);
		Assert.Equal("web01:5900 closed the connection unexpectedly.", reset.Message);
	}

	[Fact]
	public async Task DescribeAsync_AnythingElseAgainstAnAddress_KeepsWhatTheSocketSaid()
	{
		SocketException socket = new((int)SocketError.NetworkDown);

		SocketFailureReport report = await SocketFailures.DescribeAsync(
			socket,
			"127.0.0.1",
			"127.0.0.1:5900",
			Hints,
			TestContext.Current.CancellationToken);

		Assert.Equal(ConnectFailure.HostUnreachable, report.Failure);
		Assert.Equal($"Could not connect to 127.0.0.1:5900: {socket.Message}", report.Message);
	}

	[Fact]
	public async Task DescribeAsync_AnythingElseAgainstAnUnknownName_LooksTheNameUpBeforeGivingUp()
	{
		SocketFailureReport report = await SocketFailures.DescribeAsync(
			new SocketException((int)SocketError.NetworkDown),
			"no-such-host.invalid",
			"no-such-host.invalid:5900",
			Hints,
			TestContext.Current.CancellationToken);

		Assert.Equal(ConnectFailure.HostUnreachable, report.Failure);
		Assert.Equal("Could not find no-such-host.invalid. Check the host name.", report.Message);
	}

	[Fact]
	public async Task IsUnknownHostAsync_AnAddressIsNeverLookedUp() =>
		Assert.False(await SocketFailures.IsUnknownHostAsync("203.0.113.9", TestContext.Current.CancellationToken));

	[Fact]
	public async Task IsUnknownHostAsync_ANameNothingResolves() =>
		Assert.True(await SocketFailures.IsUnknownHostAsync("no-such-host.invalid", TestContext.Current.CancellationToken));

	[Fact]
	public void ToException_CarriesTheCauseTheModuleChose()
	{
		SocketException socket = new((int)SocketError.ConnectionRefused);
		InvalidOperationException wrapper = new("the library's own failure", socket);

		ProtocolConnectException failure = new SocketFailureReport(ConnectFailure.HostUnreachable, "refused").ToException(wrapper);

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal("refused", failure.Message);
		Assert.Same(wrapper, failure.InnerException);
	}

	private static Task<SocketFailureReport> DescribeAsync(SocketError error, SocketFailureHints? hints = null) =>
		SocketFailures.DescribeAsync(
			new SocketException((int)error),
			"web01",
			Endpoint,
			hints ?? Hints,
			TestContext.Current.CancellationToken);
}
