using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Local.Pty;
using Mokaterm.Modules.Local.Shells;
using Mokaterm.Modules.Local.Tests.Fakes;

namespace Mokaterm.Modules.Local.Tests;

/// <summary>
/// The provider against this computer: what it refuses before anything starts, and the Command Prompt, which every
/// Windows has, started for real through the whole path a session takes.
/// </summary>
public sealed class LocalProtocolProviderTests
{

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task Connect_AnAddressThatNamesNoShell_IsRefusedWithoutAskingForAnything()
	{
		SkipUnlessSupported();
		FakeCredentialSource credentials = new();

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => ConnectAsync("no-such-shell-here", credentials: credentials, cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Contains("no-such-shell-here", failure.Message, StringComparison.Ordinal);
		Assert.Equal(0, credentials.GetCount);
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task Connect_ACustomShellWithoutAProgram_SaysWhatToName()
	{
		SkipUnlessSupported();

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => ConnectAsync("localhost", new LocalConnectionOptions { Shell = "custom" }, cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal("Name the program this login runs in its options.", failure.Message);
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task Connect_AStartingFolderThatIsNotThere_IsRefused()
	{
		SkipUnlessSupported();
		string missing = Path.Combine(Path.GetTempPath(), $"mokaterm-missing-{Guid.NewGuid():N}");

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => ConnectAsync("cmd", new LocalConnectionOptions { StartingFolder = missing }, cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal($"The starting folder {missing} does not exist.", failure.Message);
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task Connect_RunsTheShellTheAddressNames_AndEndsCleanlyWhenItExits()
	{
		SkipUnlessSupported();
		RecordingProgress<string> status = new();
		await using IProtocolSession session = await ConnectAsync(
			"cmd",
			new LocalConnectionOptions { Arguments = "/c echo mokaterm-through-the-provider", StartingFolder = Path.GetTempPath() },
			status: status,
			cancellationToken: TestContext.Current.CancellationToken);

		ITerminalChannel terminal = session.GetFeature<ITerminalChannel>()
			?? throw new InvalidOperationException("A local session must expose a terminal channel.");

		string shown = await TerminalReading.ReadToEndAsync(terminal);

		Assert.Contains("mokaterm-through-the-provider", shown, StringComparison.Ordinal);
		Assert.DoesNotContain("[mokaterm]", shown, StringComparison.Ordinal);
		Assert.Contains("Starting Command Prompt", status.Reports);
		await Eventually.CompletesAsync(session.Completion);
		Assert.True(session.Completion.IsCompletedSuccessfully);
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task Connect_AShellThatFails_SaysItsExitCodeInTheTerminal()
	{
		SkipUnlessSupported();
		await using IProtocolSession session = await ConnectAsync(
			"cmd.exe",
			new LocalConnectionOptions { Arguments = "/c exit 7" },
			cancellationToken: TestContext.Current.CancellationToken);
		ITerminalChannel terminal = session.GetFeature<ITerminalChannel>()!;

		string shown = await TerminalReading.ReadToEndAsync(terminal);

		Assert.Contains("[mokaterm] Command Prompt exited with code 7", shown, StringComparison.Ordinal);
		await Eventually.CompletesAsync(session.Completion);
		Assert.True(session.Completion.IsCompletedSuccessfully);
	}

	// Quick connect offers what detection finds, named by the address that opens each one.
	[Fact]
	public void Suggest_OffersEveryDetectedShellByItsAddress()
	{
		TestMachine machine = TestMachine.WithWindowsShells();
		machine.Distributions.Add(new WslDistribution("Ubuntu", IsDefault: true));

		IReadOnlyList<QuickConnectSuggestion> suggestions = LocalProtocolProvider.Suggest(LocalShellDetector.Detect(machine.Build()));

		Assert.Equal(
			[
				new QuickConnectSuggestion("powershell", "Windows PowerShell"),
				new QuickConnectSuggestion("cmd", "Command Prompt"),
				new QuickConnectSuggestion("Ubuntu", "WSL distribution"),
			],
			suggestions);
	}

	[Fact]
	public void Provider_OffersQuickConnectNames_AndLeavesTheirCheckToTheConnect()
	{
		LocalProtocolProvider provider = new(new FakeSettingsService(), NullLoggerFactory.Instance);
		IQuickConnectSuggestions suggestions = provider;

		Assert.Equal("pwsh, cmd, wsl or a distro", provider.Descriptor.QuickConnectExample);
		Assert.Null(suggestions.ValidateQuickConnectAddress("no-such-shell"));
		if (!PseudoTerminalPlatform.IsSupported)
		{
			Assert.Empty(suggestions.GetQuickConnectSuggestions());
		}
	}

	private static void SkipUnlessSupported()
	{
		if (!PseudoTerminalPlatform.IsSupported)
		{
			Assert.Skip("The Windows pseudo console needs Windows 10 version 1809 or later.");
		}
	}

	private static async Task<IProtocolSession> ConnectAsync(
		string address,
		LocalConnectionOptions? options = null,
		FakeCredentialSource? credentials = null,
		RecordingProgress<string>? status = null,
		CancellationToken cancellationToken = default)
	{
		LocalProtocolProvider provider = new(new FakeSettingsService(), NullLoggerFactory.Instance);
		ProtocolConnectContext context = new ConnectContext
		{
			ProtocolId = LocalProtocolProvider.ProtocolId,
			Port = LocalProtocolProvider.NoPort,
			Credentials = credentials ?? new FakeCredentialSource(),
			Verifier = new RefusingHostVerifier(),
			Address = address,
			Options = (options ?? LocalConnectionOptions.Default).ApplyTo(ProtocolOptions.Empty),
			Status = status,
			TerminalSize = new TerminalSize(100, 30),
		}.Build();

		return await provider.ConnectAsync(context, cancellationToken).WaitAsync(Eventually.Limit, cancellationToken);
	}
}
