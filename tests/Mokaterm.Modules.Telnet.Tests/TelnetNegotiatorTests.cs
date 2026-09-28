using System.Buffers;
using System.Text;
using Mokaterm.Modules.Telnet.Protocol;

namespace Mokaterm.Modules.Telnet.Tests;

public sealed class TelnetNegotiatorTests
{
	private static readonly string[] TerminalTypes = ["xterm-256color", "xterm", "vt100"];

	[Fact]
	public void WriteInitialRequests_AsksForEveryOptionThisModuleImplements()
	{
		TelnetNegotiator negotiator = new(TerminalTypes);
		ArrayBufferWriter<byte> writer = new();

		negotiator.WriteInitialRequests(writer);

		Assert.Equal(
			[
				TelnetCommand.Iac, TelnetCommand.Do, TelnetOption.Binary,
				TelnetCommand.Iac, TelnetCommand.Do, TelnetOption.Echo,
				TelnetCommand.Iac, TelnetCommand.Do, TelnetOption.SuppressGoAhead,
				TelnetCommand.Iac, TelnetCommand.Will, TelnetOption.Binary,
				TelnetCommand.Iac, TelnetCommand.Will, TelnetOption.SuppressGoAhead,
				TelnetCommand.Iac, TelnetCommand.Will, TelnetOption.TerminalType,
				TelnetCommand.Iac, TelnetCommand.Will, TelnetOption.NegotiateAboutWindowSize,
			],
			writer.WrittenSpan.ToArray());
		Assert.False(negotiator.IsSettled);
	}

	[Fact]
	public void Handle_AnswerToOurOwnRequest_SaysNothingMore()
	{
		TelnetNegotiator negotiator = Opened();

		byte[] reply = Handle(negotiator, TelnetCommand.Will, TelnetOption.Echo);

		Assert.Empty(reply);
		Assert.True(negotiator.IsRemoteEnabled(TelnetOption.Echo));
	}

	[Fact]
	public void Handle_EveryAnswer_SettlesTheNegotiation()
	{
		TelnetNegotiator negotiator = Opened();

		Handle(negotiator, TelnetCommand.Wont, TelnetOption.Binary);
		Handle(negotiator, TelnetCommand.Will, TelnetOption.Echo);
		Handle(negotiator, TelnetCommand.Will, TelnetOption.SuppressGoAhead);
		Handle(negotiator, TelnetCommand.Dont, TelnetOption.Binary);
		Handle(negotiator, TelnetCommand.Do, TelnetOption.SuppressGoAhead);
		Handle(negotiator, TelnetCommand.Do, TelnetOption.TerminalType);
		Assert.False(negotiator.IsSettled);

		Handle(negotiator, TelnetCommand.Do, TelnetOption.NegotiateAboutWindowSize);

		Assert.True(negotiator.IsSettled);
		Assert.True(negotiator.IsLocalEnabled(TelnetOption.NegotiateAboutWindowSize));
		Assert.False(negotiator.IsLocalEnabled(TelnetOption.Binary));
	}

	[Fact]
	public void Handle_WillForAnOptionWeDoNotImplement_IsRefusedWithDont()
	{
		TelnetNegotiator negotiator = Opened();

		byte[] reply = Handle(negotiator, TelnetCommand.Will, TelnetOption.LineMode);

		Assert.Equal([TelnetCommand.Iac, TelnetCommand.Dont, TelnetOption.LineMode], reply);
		Assert.False(negotiator.IsRemoteEnabled(TelnetOption.LineMode));
	}

	[Fact]
	public void Handle_DoForAnOptionWeDoNotImplement_IsRefusedWithWont()
	{
		TelnetNegotiator negotiator = Opened();

		byte[] reply = Handle(negotiator, TelnetCommand.Do, TelnetOption.NewEnvironment);

		Assert.Equal([TelnetCommand.Iac, TelnetCommand.Wont, TelnetOption.NewEnvironment], reply);
	}

	[Fact]
	public void Handle_DoEcho_IsRefused_BecauseAClientNeverEchoesForTheServer()
	{
		TelnetNegotiator negotiator = Opened();

		byte[] reply = Handle(negotiator, TelnetCommand.Do, TelnetOption.Echo);

		Assert.Equal([TelnetCommand.Iac, TelnetCommand.Wont, TelnetOption.Echo], reply);
		Assert.False(negotiator.IsLocalEnabled(TelnetOption.Echo));
	}

	[Fact]
	public void Handle_RefusedOptionOfferedAgain_IsRefusedAgainWithoutLooping()
	{
		TelnetNegotiator negotiator = Opened();

		byte[] first = Handle(negotiator, TelnetCommand.Will, TelnetOption.Status);
		byte[] second = Handle(negotiator, TelnetCommand.Will, TelnetOption.Status);

		Assert.Equal(first, second);
		Assert.Equal(3, first.Length);
	}

	[Fact]
	public void Handle_ServerAsksForAnOptionWeAlreadyTurnedOn_SaysNothing()
	{
		TelnetNegotiator negotiator = Opened();
		Handle(negotiator, TelnetCommand.Do, TelnetOption.TerminalType);

		byte[] again = Handle(negotiator, TelnetCommand.Do, TelnetOption.TerminalType);

		Assert.Empty(again);
	}

	[Fact]
	public void Handle_ServerOffersAnOptionWeNeverAskedFor_IsAccepted()
	{
		// Nothing was requested here, so this covers the plain "server starts" path.
		TelnetNegotiator negotiator = new(TerminalTypes);

		byte[] reply = Handle(negotiator, TelnetCommand.Will, TelnetOption.Echo);

		Assert.Equal([TelnetCommand.Iac, TelnetCommand.Do, TelnetOption.Echo], reply);
		Assert.True(negotiator.IsRemoteEnabled(TelnetOption.Echo));
	}

	[Fact]
	public void Handle_DontAfterYes_TurnsTheOptionOffAgain()
	{
		TelnetNegotiator negotiator = Opened();
		Handle(negotiator, TelnetCommand.Do, TelnetOption.NegotiateAboutWindowSize);
		Assert.True(negotiator.IsLocalEnabled(TelnetOption.NegotiateAboutWindowSize));

		byte[] reply = Handle(negotiator, TelnetCommand.Dont, TelnetOption.NegotiateAboutWindowSize);

		Assert.Equal([TelnetCommand.Iac, TelnetCommand.Wont, TelnetOption.NegotiateAboutWindowSize], reply);
		Assert.False(negotiator.IsLocalEnabled(TelnetOption.NegotiateAboutWindowSize));
	}

	[Fact]
	public void Handle_TerminalTypeSend_AnswersWithTheFirstName()
	{
		TelnetNegotiator negotiator = Opened();
		Handle(negotiator, TelnetCommand.Do, TelnetOption.TerminalType);

		byte[] reply = Subnegotiation(negotiator, TelnetOption.TerminalType, [TelnetOption.Send]);

		Assert.Equal(Answer("xterm-256color"), reply);
	}

	[Fact]
	public void Handle_TerminalTypeAskedAgain_WalksTheListAndStopsAtTheLastName()
	{
		TelnetNegotiator negotiator = Opened();
		Handle(negotiator, TelnetCommand.Do, TelnetOption.TerminalType);

		Assert.Equal(Answer("xterm-256color"), Subnegotiation(negotiator, TelnetOption.TerminalType, [TelnetOption.Send]));
		Assert.Equal(Answer("xterm"), Subnegotiation(negotiator, TelnetOption.TerminalType, [TelnetOption.Send]));
		Assert.Equal(Answer("vt100"), Subnegotiation(negotiator, TelnetOption.TerminalType, [TelnetOption.Send]));
		Assert.Equal(Answer("vt100"), Subnegotiation(negotiator, TelnetOption.TerminalType, [TelnetOption.Send]));
	}

	[Fact]
	public void Handle_TerminalTypeSendBeforeTheOptionIsOn_IsIgnored()
	{
		TelnetNegotiator negotiator = Opened();

		byte[] reply = Subnegotiation(negotiator, TelnetOption.TerminalType, [TelnetOption.Send]);

		Assert.Empty(reply);
	}

	[Fact]
	public void Handle_SubnegotiationForAnotherOption_IsIgnored()
	{
		TelnetNegotiator negotiator = Opened();

		byte[] reply = Subnegotiation(negotiator, TelnetOption.NegotiateAboutWindowSize, [0, 80, 0, 24]);

		Assert.Empty(reply);
	}

	[Fact]
	public void Handle_BareCommand_IsIgnored()
	{
		TelnetNegotiator negotiator = Opened();
		ArrayBufferWriter<byte> writer = new();

		negotiator.Handle(TelnetMessage.Bare(TelnetCommand.Nop), writer);

		Assert.Equal(0, writer.WrittenCount);
	}

	[Fact]
	public void TerminalTypes_Empty_FallsBackToUnknown()
	{
		TelnetNegotiator negotiator = new([" ", ""]);

		Assert.Equal("UNKNOWN", negotiator.TerminalType);
	}

	private static TelnetNegotiator Opened()
	{
		TelnetNegotiator negotiator = new(TerminalTypes);
		negotiator.WriteInitialRequests(new ArrayBufferWriter<byte>());
		return negotiator;
	}

	private static byte[] Handle(TelnetNegotiator negotiator, byte command, byte option)
	{
		ArrayBufferWriter<byte> writer = new();
		negotiator.Handle(TelnetMessage.Negotiation(command, option), writer);
		return writer.WrittenSpan.ToArray();
	}

	private static byte[] Subnegotiation(TelnetNegotiator negotiator, byte option, byte[] payload)
	{
		ArrayBufferWriter<byte> writer = new();
		negotiator.Handle(TelnetMessage.Subnegotiation(option, payload), writer);
		return writer.WrittenSpan.ToArray();
	}

	private static byte[] Answer(string terminalType) =>
	[
		TelnetCommand.Iac, TelnetCommand.Sb, TelnetOption.TerminalType, TelnetOption.Is,
		.. Encoding.ASCII.GetBytes(terminalType),
		TelnetCommand.Iac, TelnetCommand.Se,
	];
}
