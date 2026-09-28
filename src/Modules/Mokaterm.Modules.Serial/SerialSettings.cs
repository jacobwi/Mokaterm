using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Serial.Protocol;

namespace Mokaterm.Modules.Serial;

/// <summary>
/// What belongs to the app rather than to one device: the line a new connection starts from, and the timeouts every
/// port is opened with. Stored under <c>serial</c> in settings.json.
/// </summary>
public sealed record SerialSettings : ISettingsSection
{
	public const int MinWriteTimeoutMilliseconds = 100;

	public const int MaxWriteTimeoutMilliseconds = 60_000;

	/// <summary>Shortest read timeout offered. 0, the default, waits forever instead.</summary>
	public const int MinReadTimeoutMilliseconds = 100;

	public const int MaxReadTimeoutMilliseconds = 60_000;

	public const int MaxDeviceCheckSeconds = 60;

	public const int MinBreakMilliseconds = 10;

	public const int MaxBreakMilliseconds = 2_000;

	public static string SectionKey => "serial";

	/// <summary>Baud rate a new connection starts with.</summary>
	public int BaudRate { get; init; } = SerialLineSettings.Default.BaudRate;

	public int DataBits { get; init; } = SerialLineSettings.Default.DataBits;

	public SerialParity Parity { get; init; }

	public SerialStopBits StopBits { get; init; }

	public SerialFlowControl FlowControl { get; init; }

	/// <summary>Assert DTR on a new connection. On, because most USB adapters need it.</summary>
	public bool Dtr { get; init; } = true;

	/// <summary>Assert RTS on a new connection.</summary>
	public bool Rts { get; init; } = true;

	public SerialLineEnding LineEnding { get; init; }

	/// <summary>Show typed characters on a new connection.</summary>
	public bool LocalEcho { get; init; }

	/// <summary>Character set a new connection starts with.</summary>
	public string EncodingName { get; init; } = SerialEncodings.Default;

	/// <summary>
	/// How long a write waits for the device to take the bytes. With RTS/CTS flow control a device that never
	/// raises CTS would otherwise block every keystroke for good.
	/// </summary>
	public int WriteTimeoutMilliseconds { get; init; } = 2_000;

	/// <summary>
	/// How long one read waits for the device. 0, the default, waits forever, which is what a console wants: a
	/// device that says nothing for an hour is quiet, not broken. A timeout is not an error either way.
	/// </summary>
	public int ReadTimeoutMilliseconds { get; init; }

	/// <summary>
	/// How often the pins are read while the line is quiet, which is what notices an adapter being unplugged and
	/// what keeps the session's serial panel current. 0 turns it off; the session then only ends on the next read
	/// or write.
	/// </summary>
	public int DeviceCheckSeconds { get; init; } = 2;

	/// <summary>How long the break the session panel sends holds the line low.</summary>
	public int BreakMilliseconds { get; init; } = 250;

	/// <summary>The line a new connection starts from.</summary>
	public SerialLineSettings Line => new()
	{
		BaudRate = BaudRate,
		DataBits = DataBits,
		Parity = Parity,
		StopBits = StopBits,
		FlowControl = FlowControl,
	};

	/// <summary>The options a connection that has never been edited starts from.</summary>
	public SerialConnectionOptions NewConnectionOptions => new()
	{
		Line = Line,
		Dtr = Dtr,
		Rts = Rts,
		EncodingName = EncodingName,
		LineEnding = LineEnding,
		LocalEcho = LocalEcho,
	};

	internal TimeSpan WriteTimeout => TimeSpan.FromMilliseconds(WriteTimeoutMilliseconds);

	internal TimeSpan ReadTimeout =>
		ReadTimeoutMilliseconds > 0 ? TimeSpan.FromMilliseconds(ReadTimeoutMilliseconds) : TimeSpan.Zero;

	internal TimeSpan DeviceCheck =>
		DeviceCheckSeconds > 0 ? TimeSpan.FromSeconds(DeviceCheckSeconds) : TimeSpan.Zero;

	internal TimeSpan Break => TimeSpan.FromMilliseconds(BreakMilliseconds);

	/// <summary>A copy every port can be opened with: values in range, and a frame the UART can actually do.</summary>
	public SerialSettings Clamped()
	{
		SerialSettings clamped = this with
		{
			BaudRate = Math.Clamp(BaudRate, SerialLineSettings.MinBaudRate, SerialLineSettings.MaxBaudRate),
			DataBits = Math.Clamp(DataBits, SerialLineSettings.MinDataBits, SerialLineSettings.MaxDataBits),
			Parity = Enum.IsDefined(Parity) ? Parity : SerialParity.None,
			StopBits = Enum.IsDefined(StopBits) ? StopBits : SerialStopBits.One,
			FlowControl = Enum.IsDefined(FlowControl) ? FlowControl : SerialFlowControl.None,
			LineEnding = Enum.IsDefined(LineEnding) ? LineEnding : SerialLineEnding.Cr,
			EncodingName = TerminalEncodings.TryGet(EncodingName, out _) ? EncodingName.Trim() : SerialEncodings.Default,
			WriteTimeoutMilliseconds = Math.Clamp(WriteTimeoutMilliseconds, MinWriteTimeoutMilliseconds, MaxWriteTimeoutMilliseconds),
			ReadTimeoutMilliseconds = ReadTimeoutMilliseconds <= 0
				? 0
				: Math.Clamp(ReadTimeoutMilliseconds, MinReadTimeoutMilliseconds, MaxReadTimeoutMilliseconds),
			DeviceCheckSeconds = Math.Clamp(DeviceCheckSeconds, 0, MaxDeviceCheckSeconds),
			BreakMilliseconds = Math.Clamp(BreakMilliseconds, MinBreakMilliseconds, MaxBreakMilliseconds),
		};

		// Five data bits go with one and a half stop bits, six to eight with one or two: no other frame exists.
		return clamped.StopBits switch
		{
			SerialStopBits.OnePointFive when clamped.DataBits != 5 => clamped with { StopBits = SerialStopBits.One },
			SerialStopBits.Two when clamped.DataBits == 5 => clamped with { StopBits = SerialStopBits.OnePointFive },
			_ => clamped,
		};
	}
}
