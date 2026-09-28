using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Telnet.Protocol;

namespace Mokaterm.Modules.Telnet.Tests;

public sealed class TelnetConnectionOptionsTests
{
	[Fact]
	public void Default_IsUtf8WithCrLfAndNoAutomaticLogin()
	{
		TelnetConnectionOptions options = TelnetConnectionOptions.Default;

		Assert.Null(options.TerminalType);
		Assert.Equal(TelnetEncodings.Default, options.EncodingName);
		Assert.Equal(TelnetEchoMode.Auto, options.Echo);
		Assert.Equal(TelnetLineEnding.CrLf, options.LineEnding);
		Assert.False(options.AutoLogin);
	}

	[Fact]
	public void ApplyTo_ThenFrom_KeepsEveryValue()
	{
		TelnetConnectionOptions options = new()
		{
			TerminalType = "vt220",
			EncodingName = "windows-1252",
			Echo = TelnetEchoMode.On,
			LineEnding = TelnetLineEnding.CrNul,
			AutoLogin = true,
		};

		TelnetConnectionOptions read = TelnetConnectionOptions.From(options.ApplyTo(ProtocolOptions.Empty));

		Assert.Equal(options, read);
	}

	[Fact]
	public void ApplyTo_KeepsKeysThatBelongToSomethingElse()
	{
		ProtocolOptions other = ProtocolOptions.Empty.With("ssh.startupCommand", "tmux");

		ProtocolOptions written = TelnetConnectionOptions.Default.ApplyTo(other);

		Assert.Equal("tmux", written.GetString("ssh.startupCommand"));
	}

	[Fact]
	public void ApplyTo_Defaults_WritesNoTerminalTypeAndNoAutomaticLogin()
	{
		ProtocolOptions written = TelnetConnectionOptions.Default.ApplyTo(ProtocolOptions.Empty);

		Assert.False(written.ContainsKey(TelnetConnectionOptions.TerminalTypeKey));
		Assert.False(written.ContainsKey(TelnetConnectionOptions.AutoLoginKey));
	}

	[Fact]
	public void From_UnknownEnumValues_FallBackToTheDefaults()
	{
		ProtocolOptions stored = ProtocolOptions.Empty
			.With(TelnetConnectionOptions.EchoKey, "7")
			.With(TelnetConnectionOptions.LineEndingKey, "Whatever");

		TelnetConnectionOptions options = TelnetConnectionOptions.From(stored);

		Assert.Equal(TelnetEchoMode.Auto, options.Echo);
		Assert.Equal(TelnetLineEnding.CrLf, options.LineEnding);
	}

	[Fact]
	public void From_BlankEncoding_IsUtf8()
	{
		ProtocolOptions stored = ProtocolOptions.Empty.With(TelnetConnectionOptions.EncodingKey, "   ");

		Assert.Equal(TelnetEncodings.Default, TelnetConnectionOptions.From(stored).EncodingName);
	}

	[Fact]
	public void From_TerminalTypeWithForbiddenCharacters_IsDropped()
	{
		ProtocolOptions stored = ProtocolOptions.Empty.With(TelnetConnectionOptions.TerminalTypeKey, "xterm 256color");

		Assert.Null(TelnetConnectionOptions.From(stored).TerminalType);
	}

	[Theory]
	[InlineData("xterm-256color", true)]
	[InlineData("vt100", true)]
	[InlineData("linux+kbd", true)]
	[InlineData("", false)]
	[InlineData(null, false)]
	[InlineData("two words", false)]
	[InlineData("semi;colon", false)]
	public void IsValidTerminalType_AcceptsOnlyWhatAServerWouldTake(string? terminalType, bool expected) =>
		Assert.Equal(expected, TelnetConnectionOptions.IsValidTerminalType(terminalType));

	[Fact]
	public void IsValidTerminalType_TooLong_IsRefused() =>
		Assert.False(TelnetConnectionOptions.IsValidTerminalType(new string('x', TelnetConnectionOptions.MaxTerminalTypeLength + 1)));
}
