using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Serial.Protocol;

namespace Mokaterm.Modules.Serial;

/// <summary>Typed access to the <c>serial.*</c> keys a serial connection keeps in its <see cref="ProtocolOptions"/>.</summary>
public sealed record SerialConnectionOptions
{
	/// <summary>Every key this module owns starts with this.</summary>
	public const string KeyPrefix = "serial.";

	public const string PortKey = "serial.port";

	public const string BaudRateKey = "serial.baud";

	public const string DataBitsKey = "serial.dataBits";

	public const string ParityKey = "serial.parity";

	public const string StopBitsKey = "serial.stopBits";

	public const string FlowControlKey = "serial.flowControl";

	public const string DtrKey = "serial.dtr";

	public const string RtsKey = "serial.rts";

	public const string EncodingKey = "serial.encoding";

	public const string LineEndingKey = "serial.lineEnding";

	public const string LocalEchoKey = "serial.localEcho";

	public static SerialConnectionOptions Default { get; } = new();

	/// <summary>
	/// The port to open. Empty opens the port named by the host's address, which is the normal way: a serial login's
	/// machine <em>is</em> its port. This overrides it for a machine where the same adapter shows up under another
	/// name.
	/// </summary>
	public string? PortName { get; init; }

	public SerialLineSettings Line { get; init; } = SerialLineSettings.Default;

	/// <summary>Assert DTR while the port is open. On by default; many USB adapters need it to transmit at all.</summary>
	public bool Dtr { get; init; } = true;

	/// <summary>Assert RTS while the port is open. Ignored when RTS/CTS flow control drives the pin.</summary>
	public bool Rts { get; init; } = true;

	/// <summary>Character set on the line, such as <c>utf-8</c>, <c>windows-1252</c> or <c>ibm437</c>.</summary>
	public string EncodingName { get; init; } = SerialEncodings.Default;

	public SerialLineEnding LineEnding { get; init; }

	/// <summary>Show what is typed. Needed by devices that echo nothing, which is most bootloaders.</summary>
	public bool LocalEcho { get; init; }

	/// <summary>True when the connection carries any serial key, so a new one can start from the saved defaults.</summary>
	public static bool HasAny(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return options.Keys.Any(key => key.StartsWith(KeyPrefix, StringComparison.Ordinal));
	}

	public static SerialConnectionOptions From(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		string port = SerialPortNames.Normalize(options.GetString(PortKey));
		string? encoding = options.GetString(EncodingKey);
		return new SerialConnectionOptions
		{
			PortName = port.Length == 0 ? null : port,
			Line = new SerialLineSettings
			{
				BaudRate = options.GetInt32(BaudRateKey, SerialLineSettings.Default.BaudRate),
				DataBits = options.GetInt32(DataBitsKey, SerialLineSettings.Default.DataBits),
				Parity = options.GetEnum(ParityKey, SerialParity.None),
				StopBits = options.GetEnum(StopBitsKey, SerialStopBits.One),
				FlowControl = options.GetEnum(FlowControlKey, SerialFlowControl.None),
			},
			Dtr = options.GetBoolean(DtrKey, true),
			Rts = options.GetBoolean(RtsKey, true),
			EncodingName = string.IsNullOrWhiteSpace(encoding) ? SerialEncodings.Default : encoding,
			LineEnding = options.GetEnum(LineEndingKey, SerialLineEnding.Cr),
			LocalEcho = options.GetBoolean(LocalEchoKey, false),
		};
	}

	/// <summary>Writes these values over <paramref name="options"/>, keeping keys that belong to anything else.</summary>
	public ProtocolOptions ApplyTo(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		string port = SerialPortNames.Normalize(PortName);
		return options
			.With(PortKey, port.Length == 0 ? null : port)
			.With(BaudRateKey, Line.BaudRate)
			.With(DataBitsKey, Line.DataBits)
			.WithEnum<SerialParity>(ParityKey, Line.Parity)
			.WithEnum<SerialStopBits>(StopBitsKey, Line.StopBits)
			.WithEnum<SerialFlowControl>(FlowControlKey, Line.FlowControl)
			.With(DtrKey, Dtr)
			.With(RtsKey, Rts)
			.With(EncodingKey, string.IsNullOrWhiteSpace(EncodingName) ? null : EncodingName)
			.WithEnum<SerialLineEnding>(LineEndingKey, LineEnding)
			.With(LocalEchoKey, LocalEcho);
	}

	/// <summary>The port this connection opens: its own name, or the host's address when it names none.</summary>
	public string ResolvePortName(string hostAddress) =>
		PortName is { Length: > 0 } port ? port : SerialPortNames.Normalize(hostAddress);

	/// <summary>A message naming what cannot be opened, or null when the connection is usable.</summary>
	public string? Validate(string hostAddress) =>
		SerialPortNames.Validate(ResolvePortName(hostAddress)) ?? Line.Validate();
}
