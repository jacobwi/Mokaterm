using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Local.Sessions;
using Mokaterm.Modules.Local.Tests.Fakes;

namespace Mokaterm.Modules.Local.Tests;

public sealed class LocalTerminalChannelTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Output_ReachesTheView_AsTheProgramWroteIt()
	{
		(LocalTerminalChannel channel, FakePseudoTerminal terminal) = Open();
		await using LocalTerminalChannel owned = channel;

		Assert.True(terminal.Print("PS C:\\> "));
		Assert.True(terminal.Print("\u001b[32mgreen\u001b[0m"));

		string shown = await TerminalReading.ReadUntilAsync(channel, "green");

		Assert.Equal("PS C:\\> \u001b[32mgreen", shown[..(shown.IndexOf("green", StringComparison.Ordinal) + 5)]);
	}

	[Fact]
	public async Task Input_AndResizes_GoToTheTerminalUntouched()
	{
		(LocalTerminalChannel channel, FakePseudoTerminal terminal) = Open();
		await using LocalTerminalChannel owned = channel;

		await channel.WriteAsync(Encoding.UTF8.GetBytes("dir\r"), Ct);
		await channel.ResizeAsync(new TerminalSize(132, 43), Ct);

		// Enter stays a carriage return: the pseudo console reads VT input the way a terminal sends it.
		Assert.Equal("dir\r", terminal.Typed);
		Assert.Equal([new TerminalSize(132, 43)], terminal.Sizes);
	}

	[Fact]
	public async Task AnExitWithZero_EndsTheChannelCleanly_WithNoNotice()
	{
		(LocalTerminalChannel channel, FakePseudoTerminal terminal) = Open();
		await using LocalTerminalChannel owned = channel;

		terminal.Print("bye\r\n");
		terminal.End(0);

		Assert.Equal("bye\r\n", await TerminalReading.ReadToEndAsync(channel));
		await Eventually.CompletesAsync(channel.Ended);
		Assert.Null(channel.Failure);
		Assert.Equal(0, channel.ExitCode);
	}

	[Fact]
	public async Task AFailingExitCode_IsWrittenAfterTheLastOutput()
	{
		(LocalTerminalChannel channel, FakePseudoTerminal terminal) = Open();
		await using LocalTerminalChannel owned = channel;

		terminal.Print("not recognized\r\n");
		terminal.End(9009);

		string shown = await TerminalReading.ReadToEndAsync(channel);

		Assert.StartsWith("not recognized\r\n", shown, StringComparison.Ordinal);
		Assert.Contains("[mokaterm] Command Prompt exited with code 9009", shown, StringComparison.Ordinal);
		Assert.Null(channel.Failure);
	}

	[Fact]
	public async Task AnNtStatusExitCode_IsWrittenInHexAsWell()
	{
		(LocalTerminalChannel channel, FakePseudoTerminal terminal) = Open();
		await using LocalTerminalChannel owned = channel;

		terminal.End(unchecked((int)0xC000013A));

		Assert.Contains("exited with code -1073741510 (0xC000013A)", await TerminalReading.ReadToEndAsync(channel), StringComparison.Ordinal);
	}

	[Fact]
	public async Task AFailingExitCode_IsNotWritten_WhenSettingsSayNot()
	{
		(LocalTerminalChannel channel, FakePseudoTerminal terminal) = Open(reportExitCode: false);
		await using LocalTerminalChannel owned = channel;

		terminal.End(1);

		Assert.Equal("", await TerminalReading.ReadToEndAsync(channel));
	}

	[Fact]
	public async Task AConsoleThatGoesAway_UnderARunningProgram_IsAFailure()
	{
		(LocalTerminalChannel channel, FakePseudoTerminal terminal) = Open();
		await using LocalTerminalChannel owned = channel;

		terminal.End(null);

		await Eventually.CompletesAsync(channel.Ended);
		IOException failure = Assert.IsType<IOException>(channel.Failure);
		Assert.Contains("Command Prompt", failure.Message, StringComparison.Ordinal);
		Assert.Null(channel.ExitCode);
	}

	[Fact]
	public async Task Dispose_EndsTheTerminal_AndRefusesItsOutputFromThenOn()
	{
		(LocalTerminalChannel channel, FakePseudoTerminal terminal) = Open();

		await channel.DisposeAsync();

		Assert.True(terminal.Disposed);
		Assert.True(channel.Ended.IsCompleted);
		Assert.Null(channel.Failure);

		// The terminal's reader is told nobody wants output, so it drains the console instead of waiting on a view.
		Assert.False(terminal.Print("late"));
		Assert.Equal(0, await channel.ReadAsync(new byte[16], Ct));

		await channel.WriteAsync(Encoding.UTF8.GetBytes("ignored"), Ct);
		Assert.Equal("", terminal.Typed);
	}

	[Fact]
	public async Task Session_ExposesOnlyTheTerminal_AndCompletesCleanlyWhenTheShellExits()
	{
		(LocalTerminalChannel channel, FakePseudoTerminal terminal) = Open();
		await using LocalSession session = new(Guid.NewGuid(), channel, NullLogger.Instance);

		Assert.Same(channel, session.GetFeature<ITerminalChannel>());
		Assert.Null(session.GetFeature<IFileSystemFeature>());

		terminal.End(2);

		// Any exit is a clean end: a reconnect is the user's call, so a program that fails at once is not restarted forever.
		await Eventually.CompletesAsync(session.Completion);
		Assert.True(session.Completion.IsCompletedSuccessfully);
	}

	[Fact]
	public async Task Session_FaultsWhenTheConsoleIsLost()
	{
		(LocalTerminalChannel channel, FakePseudoTerminal terminal) = Open();
		await using LocalSession session = new(Guid.NewGuid(), channel, NullLogger.Instance);

		terminal.End(null);

		await Assert.ThrowsAsync<IOException>(() => session.Completion.WaitAsync(Eventually.Limit, Ct));
	}

	[Fact]
	public async Task Session_Dispose_ClosesTheTerminal()
	{
		(LocalTerminalChannel channel, FakePseudoTerminal terminal) = Open();
		LocalSession session = new(Guid.NewGuid(), channel, NullLogger.Instance);

		await session.DisposeAsync();
		await session.DisposeAsync();

		Assert.True(terminal.Disposed);
		Assert.True(session.Completion.IsCompletedSuccessfully);
	}

	private static (LocalTerminalChannel Channel, FakePseudoTerminal Terminal) Open(bool reportExitCode = true)
	{
		LocalTerminalChannel channel = new("Command Prompt", reportExitCode, NullLogger.Instance);
		FakePseudoTerminal terminal = new(channel);
		channel.Attach(terminal);
		return (channel, terminal);
	}
}
