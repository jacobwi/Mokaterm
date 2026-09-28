using System.Text;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Telnet.Protocol;
using Mokaterm.Modules.Telnet.Tests.Fakes;

namespace Mokaterm.Modules.Telnet.Tests.Loopback;

public sealed class TelnetTerminalTests
{
	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Read_ServerData_ArrivesWithoutTheProtocolBytes()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(server, cancellationToken);

		// A negotiation in the middle of the text must not reach the terminal.
		await telnet.Peer.SendTextAsync("one", cancellationToken);
		await telnet.Peer.SendNegotiationAsync(TelnetCommand.Will, TelnetOption.SuppressGoAhead, cancellationToken);
		await telnet.Peer.SendTextAsync("two", cancellationToken);

		string text = await TelnetHarness.ReadTextAsync(telnet.Terminal, "onetwo", cancellationToken);

		Assert.Equal("onetwo", text);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Read_DoubledIac_ArrivesAsOneByte()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(server, cancellationToken);

		await telnet.Peer.SendAsync(new byte[] { (byte)'a', TelnetCommand.Iac, TelnetCommand.Iac, (byte)'b' }, cancellationToken);
		byte[] received = await ReadAsync(telnet, 3, cancellationToken);

		Assert.Equal([(byte)'a', 255, (byte)'b'], received);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Read_CarriageReturnNul_ReachesTheTerminalAsACarriageReturn()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(server, cancellationToken);

		await telnet.Peer.SendAsync(new byte[] { (byte)'a', 0x0D, 0x00, (byte)'b' }, cancellationToken);
		byte[] received = await ReadAsync(telnet, 3, cancellationToken);

		Assert.Equal([(byte)'a', 0x0D, (byte)'b'], received);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Write_Iac_IsDoubledOnTheWire()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(server, cancellationToken);

		await telnet.Terminal.WriteAsync(new byte[] { (byte)'a', 255, (byte)'b' }, cancellationToken);

		// The peer unescapes what it reads, so a single 255 here means two went over the wire.
		Assert.Equal([(byte)'a', 255, (byte)'b'], await telnet.Peer.ReadDataAsync(3, cancellationToken));
	}

	[Theory]
	[InlineData(TelnetLineEnding.CrLf, new byte[] { 0x0D, 0x0A })]
	[InlineData(TelnetLineEnding.CrNul, new byte[] { 0x0D, 0x00 })]
	[InlineData(TelnetLineEnding.Lf, new byte[] { 0x0A })]
	public async Task Write_Enter_SendsTheLineEndingTheConnectionUses(TelnetLineEnding lineEnding, byte[] expected)
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(
			server,
			cancellationToken,
			options: TelnetConnectionOptions.Default with { LineEnding = lineEnding });

		await telnet.Terminal.WriteAsync("ls\r"u8.ToArray(), cancellationToken);
		byte[] sent = await telnet.Peer.ReadDataAsync(2 + expected.Length, cancellationToken);

		Assert.Equal([(byte)'l', (byte)'s', .. expected], sent);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Encoding_CodePage_IsTranscodedBothWays()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(
			server,
			cancellationToken,
			options: TelnetConnectionOptions.Default with { EncodingName = "windows-1252" });

		await telnet.Peer.SendTextAsync("café", TerminalEncodings.Resolve("windows-1252"), cancellationToken);
		string shown = await TelnetHarness.ReadTextAsync(telnet.Terminal, "café", cancellationToken);

		await telnet.Terminal.WriteAsync(Encoding.UTF8.GetBytes("café"), cancellationToken);
		byte[] wire = await telnet.Peer.ReadDataAsync(4, cancellationToken);

		Assert.Equal("café", shown);
		Assert.Equal([(byte)'c', (byte)'a', (byte)'f', 0xE9], wire);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task LocalEcho_ShowsTypingUntilTheServerTurnsEchoOn()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(server, cancellationToken);

		await telnet.Terminal.WriteAsync("hi\r"u8.ToArray(), cancellationToken);
		Assert.Equal("hi\r\n", await telnet.Peer.ReadTextAsync(4, cancellationToken));
		string echoed = await TelnetHarness.ReadTextAsync(telnet.Terminal, "hi\r\n", cancellationToken);

		// Once the server echoes, the client must stop, or every character would show twice.
		await telnet.Peer.SendNegotiationAsync(TelnetCommand.Will, TelnetOption.Echo, cancellationToken);
		await telnet.Peer.ReadMessageAsync(TelnetOption.Echo, cancellationToken);
		await telnet.Terminal.WriteAsync("x"u8.ToArray(), cancellationToken);
		await telnet.Peer.ReadDataAsync(1, cancellationToken);
		await telnet.Peer.SendTextAsync("done", cancellationToken);
		string afterwards = await TelnetHarness.ReadTextAsync(telnet.Terminal, "done", cancellationToken);

		Assert.Equal("hi\r\n", echoed);
		Assert.Equal("done", afterwards);
	}

	[Theory(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	[InlineData((byte)0x08)]
	[InlineData((byte)0x7F)]
	public async Task LocalEcho_AnEraseKey_TakesTheCharacterOffTheScreen(byte key)
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(server, cancellationToken);

		// The key itself is data and goes out untouched; what the screen does with it is ours while the server is quiet.
		await telnet.Terminal.WriteAsync(new byte[] { (byte)'a', key }, cancellationToken);
		Assert.Equal([(byte)'a', key], await telnet.Peer.ReadDataAsync(2, cancellationToken));

		// The marker is read after the echo, so the whole screen can be compared instead of waited for.
		await telnet.Peer.SendTextAsync("Z", cancellationToken);
		string echoed = await TelnetHarness.ReadTextAsync(telnet.Terminal, "Z", cancellationToken);

		Assert.Equal("a\b \bZ", echoed);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task LocalEcho_TurnedOff_ShowsNothingOfWhatIsTyped()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(
			server,
			cancellationToken,
			options: TelnetConnectionOptions.Default with { Echo = TelnetEchoMode.Off });

		await telnet.Terminal.WriteAsync("hi"u8.ToArray(), cancellationToken);
		await telnet.Peer.ReadDataAsync(2, cancellationToken);
		await telnet.Peer.SendTextAsync("only this", cancellationToken);

		Assert.Equal("only this", await TelnetHarness.ReadTextAsync(telnet.Terminal, "only this", cancellationToken));
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task AutoLogin_TypesTheSavedLoginAtThePromptAndNeverShowsThePassword()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		FakeCredentialSource credentials = new("operator", "s3cret", AuthenticationMethod.Password);
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(
			server,
			cancellationToken,
			options: TelnetConnectionOptions.Default with { AutoLogin = true },
			credentials: credentials);

		await telnet.Peer.SendTextAsync("\r\nUsername: ", cancellationToken);
		Assert.Equal("operator\r\n", await telnet.Peer.ReadTextAsync(10, cancellationToken));

		await telnet.Peer.SendTextAsync("\r\nPassword: ", cancellationToken);
		Assert.Equal("s3cret\r\n", await telnet.Peer.ReadTextAsync(8, cancellationToken));

		await telnet.Peer.SendTextAsync("\r\nwelcome\r\n", cancellationToken);
		string shown = await TelnetHarness.ReadTextAsync(telnet.Terminal, "welcome", cancellationToken);

		Assert.Equal(1, credentials.GetCount);
		Assert.Contains("operator", shown, StringComparison.Ordinal);
		Assert.DoesNotContain("s3cret", shown, StringComparison.Ordinal);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task AutoLogin_UserTakesOverAfterTheName_NeverTypesThePassword()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		FakeCredentialSource credentials = new("operator", "s3cret", AuthenticationMethod.Password);
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(
			server,
			cancellationToken,
			options: TelnetConnectionOptions.Default with { AutoLogin = true },
			credentials: credentials);

		await telnet.Peer.SendTextAsync("\r\nUsername: ", cancellationToken);
		Assert.Equal("operator\r\n", await telnet.Peer.ReadTextAsync(10, cancellationToken));

		// The account logs straight in, and the user runs su before the automatic login gave up.
		await telnet.Peer.SendTextAsync("\r\n$ ", cancellationToken);
		await TelnetHarness.ReadTextAsync(telnet.Terminal, "$ ", cancellationToken);
		await telnet.Terminal.WriteAsync("su -\r"u8.ToArray(), cancellationToken);
		Assert.Equal("su -\r\n", await telnet.Peer.ReadTextAsync(6, cancellationToken));

		// The read loop takes one chunk at a time: once the marker shows, su's prompt has been through the watcher.
		await telnet.Peer.SendTextAsync("Password: ", cancellationToken);
		await TelnetHarness.ReadTextAsync(telnet.Terminal, "Password: ", cancellationToken);
		await telnet.Peer.SendTextAsync("\r\nmarker", cancellationToken);
		await TelnetHarness.ReadTextAsync(telnet.Terminal, "marker", cancellationToken);
		await telnet.Terminal.WriteAsync("z\r"u8.ToArray(), cancellationToken);

		Assert.Equal("z\r\n", await telnet.Peer.ReadTextAsync(3, cancellationToken));
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task AutoLogin_ServerThatNeverAsks_TypesNothing()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		FakeCredentialSource credentials = new("operator", "s3cret", AuthenticationMethod.Password);
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(
			server,
			cancellationToken,
			options: TelnetConnectionOptions.Default with { AutoLogin = true },
			credentials: credentials);

		await telnet.Peer.SendTextAsync("no prompt here\r\n", cancellationToken);
		await TelnetHarness.ReadTextAsync(telnet.Terminal, "no prompt here", cancellationToken);

		await telnet.Terminal.WriteAsync("k"u8.ToArray(), cancellationToken);

		Assert.Equal("k", await telnet.Peer.ReadTextAsync(1, cancellationToken));
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Session_ServerCloses_EndsTheTerminalAndCompletesTheSession()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(server, cancellationToken);
		await telnet.Peer.SendTextAsync("bye\r\n", cancellationToken);
		await TelnetHarness.ReadTextAsync(telnet.Terminal, "bye", cancellationToken);

		telnet.Peer.Close();

		await telnet.Session.Completion.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
		Assert.Equal(0, await telnet.Terminal.ReadAsync(new byte[16], cancellationToken));
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Session_HasATerminalAndNoFileSystem()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(server, cancellationToken);

		Assert.NotNull(telnet.Session.GetFeature<ITerminalChannel>());
		Assert.Null(telnet.Session.GetFeature<IFileSystemFeature>());
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Session_Disposed_StopsReadingAndWriting()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		TelnetTestSession telnet = await TelnetHarness.OpenAsync(server, cancellationToken);

		await telnet.Session.DisposeAsync();

		Assert.Equal(0, await telnet.Terminal.ReadAsync(new byte[16], cancellationToken));
		await telnet.Terminal.WriteAsync("x"u8.ToArray(), cancellationToken);
		await telnet.DisposeAsync();
	}

	private static async Task<byte[]> ReadAsync(TelnetTestSession telnet, int count, CancellationToken cancellationToken)
	{
		List<byte> received = [];
		byte[] buffer = new byte[64];
		while (received.Count < count)
		{
			int read = await telnet.Terminal.ReadAsync(buffer, cancellationToken);
			if (read == 0)
			{
				break;
			}

			received.AddRange(buffer.AsSpan(0, read));
		}

		return [.. received];
	}
}
