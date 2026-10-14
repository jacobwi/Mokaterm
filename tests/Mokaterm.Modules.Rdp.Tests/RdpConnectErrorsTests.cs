using System.Net.Sockets;
using System.Security.Authentication;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>
/// What a failed RDP connect tells the user, step by step. The sentences are asserted and not only the failure kinds:
/// "it timed out" is what the first real test against a Windows PC said, and it did not say where.
/// </summary>
public sealed class RdpConnectErrorsTests
{
	[Fact]
	public async Task DescribeAsync_ARefusedPort_SaysRemoteDesktopHasToBeTurnedOn()
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)SocketError.ConnectionRefused));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal(
			"term01:3389 refused the connection. Remote Desktop listens on 3389 and has to be turned on for the account.",
			failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_AnIpv6Address_KeepsItsBracketsSoThePortIsReadable()
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)SocketError.ConnectionRefused), host: "2001:db8::5");

		Assert.StartsWith("[2001:db8::5]:3389 refused the connection.", failure.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(SocketError.NetworkUnreachable)]
	[InlineData(SocketError.HostUnreachable)]
	public async Task DescribeAsync_NoRouteToTheDesktop_SaysSo(SocketError error)
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)error));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal("There is no route to term01:3389.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_AnUnknownHost_SaysToCheckTheName()
	{
		ProtocolConnectException failure = await DescribeAsync(new SocketException((int)SocketError.HostNotFound));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal("Could not find term01. Check the host name.", failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_ASocketFailureInsideAnIoException_IsReadThroughIt()
	{
		ProtocolConnectException failure = await DescribeAsync(
			new IOException("broken", new SocketException((int)SocketError.ConnectionReset)),
			RdpConnectStage.Tls);

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Equal("term01:3389 closed the connection unexpectedly.", failure.Message);
	}

	// The case the first real test hit: nothing answers on 3389, which a Windows firewall does for a closed port.
	[Fact]
	public async Task DescribeAsync_ADialThatTimesOut_SaysWhatHasToBeOnAtTheOtherEnd()
	{
		ProtocolConnectException failure = await DescribeAsync(new OperationCanceledException(), RdpConnectStage.Dialing, timedOut: true);

		Assert.Equal(ConnectFailure.Timeout, failure.Failure);
		Assert.StartsWith("term01:3389 did not answer in time. Remote Desktop has to be turned on", failure.Message, StringComparison.Ordinal);
		Assert.Contains("firewall", failure.Message, StringComparison.Ordinal);
		Assert.Contains("Windows Home", failure.Message, StringComparison.Ordinal);
		Assert.Contains("3389", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_ASocketThatTimedOutByItself_ReadsLikeOurOwnDeadline()
	{
		ProtocolConnectException ours = await DescribeAsync(new OperationCanceledException(), RdpConnectStage.Dialing, timedOut: true);
		ProtocolConnectException windows = await DescribeAsync(new SocketException((int)SocketError.TimedOut));

		Assert.Equal(ours.Message, windows.Message);
		Assert.Equal(ConnectFailure.Timeout, windows.Failure);
	}

	[Theory]
	[InlineData((int)RdpConnectStage.Negotiating, "term01:3389 took the connection but did not answer the RDP request in time. Check that this port belongs to Remote Desktop.")]
	[InlineData((int)RdpConnectStage.Tls, "term01:3389 did not finish the TLS handshake in time.")]
	[InlineData((int)RdpConnectStage.Login, "term01:3389 did not finish checking the login (CredSSP) in time.")]
	[InlineData((int)RdpConnectStage.Session, "term01:3389 did not finish setting up the session in time.")]
	public async Task DescribeAsync_ATimeout_NamesTheStepItHappenedIn(int stage, string message)
	{
		ProtocolConnectException failure = await DescribeAsync(new OperationCanceledException(), (RdpConnectStage)stage, timedOut: true);

		Assert.Equal(ConnectFailure.Timeout, failure.Failure);
		Assert.Equal(message, failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_ASessionThatStalls_NamesTheConnectorStep()
	{
		RdpConnectProgress progress = Progress(RdpConnectStage.Session);
		progress.Note("LicensingExchange");

		ProtocolConnectException failure = await RdpConnectErrors.DescribeAsync(
			new OperationCanceledException(),
			progress,
			timedOut: true,
			TestContext.Current.CancellationToken);

		Assert.Equal(
			"term01:3389 did not finish setting up the session in time. It stopped answering at LicensingExchange.",
			failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_AKerberosServerThatDoesNotAnswer_NamesItAndTheWayAround()
	{
		RdpConnectProgress progress = Progress(RdpConnectStage.Kerberos, "dc01.corp.example:88");

		ProtocolConnectException failure = await RdpConnectErrors.DescribeAsync(
			new OperationCanceledException(),
			progress,
			timedOut: true,
			TestContext.Current.CancellationToken);

		Assert.Equal(ConnectFailure.Timeout, failure.Failure);
		Assert.Contains("dc01.corp.example:88", failure.Message, StringComparison.Ordinal);
		Assert.Contains("PCNAME\\user", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DescribeAsync_AFailedTlsHandshake_NamesTheEndpoint()
	{
		ProtocolConnectException failure = await DescribeAsync(new AuthenticationException("no cipher"), RdpConnectStage.Tls);

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Equal("The TLS handshake with term01:3389 failed: no cipher", failure.Message);
	}

	[Theory]
	[InlineData((int)RdpConnectStage.Negotiating, "term01:3389 closed the connection without answering the RDP request. Check that this port belongs to Remote Desktop.")]
	[InlineData((int)RdpConnectStage.Tls, "term01:3389 closed the connection during the TLS handshake.")]
	[InlineData((int)RdpConnectStage.Login, "term01:3389 closed the connection while checking the login.")]
	[InlineData((int)RdpConnectStage.Session, "term01:3389 closed the connection while setting up the session.")]
	public async Task DescribeAsync_AServerThatHangsUp_SaysWhenItDid(int stage, string message)
	{
		ProtocolConnectException failure = await DescribeAsync(new EndOfStreamException(), (RdpConnectStage)stage);

		Assert.Equal(ConnectFailure.ProtocolError, failure.Failure);
		Assert.Equal(message, failure.Message);
	}

	[Fact]
	public async Task DescribeAsync_MapsTheRestOfWhatAUserMeets()
	{
		Assert.Equal(ConnectFailure.Timeout, (await DescribeAsync(new TimeoutException())).Failure);
		Assert.Equal(ConnectFailure.AuthenticationFailed, (await DescribeAsync(new RdpAuthenticationException("no"))).Failure);
		Assert.Equal(ConnectFailure.ProtocolError, (await DescribeAsync(new InvalidDataException("odd"), RdpConnectStage.Session)).Failure);
		Assert.Equal(ConnectFailure.Unknown, (await DescribeAsync(new InvalidOperationException("odd"))).Failure);
	}

	[Fact]
	public async Task DescribeAsync_KeepsAFailureThatAlreadySpeaksForItself()
	{
		ProtocolConnectException original = new(ConnectFailure.HostIdentityRejected, "no thanks");

		ProtocolConnectException described = await DescribeAsync(original, timedOut: true);

		Assert.Same(original, described);
	}

	[Fact]
	public void IsLoginRefused_OnlyCountsAHangUpOnceTheLoginWentOut()
	{
		Assert.True(RdpConnectErrors.IsLoginRefused(new RdpAuthenticationException("refused"), 0));
		Assert.False(RdpConnectErrors.IsLoginRefused(new EndOfStreamException(), 1));
		Assert.True(RdpConnectErrors.IsLoginRefused(new EndOfStreamException(), 2));
		Assert.True(RdpConnectErrors.IsLoginRefused(new IOException("reset", new SocketException((int)SocketError.ConnectionReset)), 2));
		Assert.False(RdpConnectErrors.IsLoginRefused(new SocketException((int)SocketError.ConnectionRefused), 2));
		Assert.False(RdpConnectErrors.IsLoginRefused(new InvalidOperationException("odd"), 2));
	}

	[Fact]
	public void LoginRefused_ABarePrincipalName_PointsAtThePcsOwnNameForTheAccount()
	{
		RdpConnectProgress progress = Progress(RdpConnectStage.Login);

		string bare = RdpConnectErrors.LoginRefused(new EndOfStreamException(), progress, RdpLogin.Parse("you@example.com", null, null));
		string local = RdpConnectErrors.LoginRefused(new EndOfStreamException(), progress, RdpLogin.Parse("alice", null, null));
		string written = RdpConnectErrors.LoginRefused(
			new EndOfStreamException(),
			progress,
			RdpLogin.Parse("MicrosoftAccount\\you@example.com", null, null));

		Assert.Contains("run whoami", bare, StringComparison.Ordinal);
		Assert.Contains("not its PIN", bare, StringComparison.Ordinal);
		Assert.DoesNotContain("whoami", local, StringComparison.Ordinal);
		Assert.DoesNotContain("whoami", written, StringComparison.Ordinal);
		Assert.StartsWith("term01:3389 closed the connection once it had the login", local, StringComparison.Ordinal);
	}

	private static RdpConnectProgress Progress(RdpConnectStage stage, string? detail = null, string host = "term01")
	{
		RdpConnectProgress progress = new(host, 3389, null);
		progress.Enter(stage, detail);
		return progress;
	}

	private static Task<ProtocolConnectException> DescribeAsync(
		Exception exception,
		RdpConnectStage stage = RdpConnectStage.Dialing,
		bool timedOut = false,
		string host = "term01") =>
		RdpConnectErrors.DescribeAsync(exception, Progress(stage, host: host), timedOut, TestContext.Current.CancellationToken);
}
