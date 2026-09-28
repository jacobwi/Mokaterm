using System.Diagnostics.CodeAnalysis;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Telnet.Protocol;

namespace Mokaterm.Modules.Telnet;

/// <summary>Typed access to the <c>telnet.*</c> keys a telnet connection keeps in its <see cref="ProtocolOptions"/>.</summary>
public sealed record TelnetConnectionOptions
{
	public const string TerminalTypeKey = "telnet.terminalType";

	public const string EncodingKey = "telnet.encoding";

	public const string EchoKey = "telnet.echo";

	public const string LineEndingKey = "telnet.lineEnding";

	public const string AutoLoginKey = "telnet.autoLogin";

	/// <summary>Longest TERMINAL-TYPE name accepted. Real names are far shorter.</summary>
	public const int MaxTerminalTypeLength = 64;

	public static TelnetConnectionOptions Default { get; } = new();

	/// <summary>Sent as the terminal type. Empty uses the name the terminal settings already send over SSH.</summary>
	public string? TerminalType { get; init; }

	/// <summary>Character set on the wire, such as <c>utf-8</c>, <c>windows-1252</c> or <c>ibm437</c>.</summary>
	public string EncodingName { get; init; } = TelnetEncodings.Default;

	public TelnetEchoMode Echo { get; init; }

	public TelnetLineEnding LineEnding { get; init; }

	/// <summary>
	/// Type the saved user name and password when the stream shows a login prompt. Off by default: telnet has no
	/// authentication of its own, so everything it sends, the password included, travels in clear text.
	/// </summary>
	public bool AutoLogin { get; init; }

	public static TelnetConnectionOptions From(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		string? terminalType = options.GetString(TerminalTypeKey);
		string? encoding = options.GetString(EncodingKey);
		return new TelnetConnectionOptions
		{
			TerminalType = IsValidTerminalType(terminalType) ? terminalType : null,
			EncodingName = string.IsNullOrWhiteSpace(encoding) ? TelnetEncodings.Default : encoding,
			Echo = options.GetEnum(EchoKey, TelnetEchoMode.Auto),
			LineEnding = options.GetEnum(LineEndingKey, TelnetLineEnding.CrLf),
			AutoLogin = options.GetBoolean(AutoLoginKey, false),
		};
	}

	/// <summary>Writes these values over <paramref name="options"/>, keeping keys that belong to anything else.</summary>
	public ProtocolOptions ApplyTo(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return options
			.With(TerminalTypeKey, IsValidTerminalType(TerminalType) ? TerminalType : null)
			.With(EncodingKey, string.IsNullOrWhiteSpace(EncodingName) ? null : EncodingName)
			.WithEnum<TelnetEchoMode>(EchoKey, Echo)
			.WithEnum<TelnetLineEnding>(LineEndingKey, LineEnding)
			.With(AutoLoginKey, AutoLogin ? "true" : null);
	}

	/// <summary>True for TERMINAL-TYPE values servers accept, such as <c>xterm-256color</c>.</summary>
	public static bool IsValidTerminalType([NotNullWhen(true)] string? terminalType) =>
		!string.IsNullOrWhiteSpace(terminalType)
		&& terminalType.Length <= MaxTerminalTypeLength
		&& terminalType.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '+');
}
