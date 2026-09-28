using System.Diagnostics.CodeAnalysis;
using System.IO.Ports;

namespace Mokaterm.Modules.Serial.Protocol;

/// <summary>
/// The only type that touches <see cref="SerialPort"/>. Everything above it works against
/// <see cref="ISerialLink"/>, so the platform specific half of this module is one file.
/// </summary>
/// <remarks>
/// Only <see cref="SerialPort.BaseStream"/> is used, never the text members, so the port's own
/// <c>Encoding</c> and <c>NewLine</c> never come into it: the line's character set and line ending are this
/// module's business.
/// </remarks>
[SuppressMessage(
	"Interoperability",
	"CA1416:Validate platform compatibility",
	Justification = "System.IO.Ports is unsupported on browser, Android, iOS and tvOS. SerialPlatform.IsSupported is checked in Open before an instance exists, and the module is only registered by hosts that run on a desktop operating system.")]
internal sealed class SerialPortLink : ISerialLink
{
	private readonly SerialPort _port;

	// Reading RtsEnable back is an error while RTS/CTS flow control owns the pin, so what we asked for is kept here.
	private bool _dtr;
	private bool _rts;
	private bool _break;
	private bool _pinsUnsupported;
	private int _disposed;

	private SerialPortLink(SerialPort port, SerialLineSettings line, bool dtr, bool rts)
	{
		_port = port;
		_dtr = dtr;
		_rts = rts;
		Line = line;
		PortName = port.PortName;
		Stream = port.BaseStream;
	}

	public string PortName { get; }

	public SerialLineSettings Line { get; }

	public Stream Stream { get; }

	public bool CanSetRts => Line.CanSetRts;

	public bool ReportsPins => !_pinsUnsupported;

	/// <summary>Opens the port. The caller turns whatever this throws into a message for the user.</summary>
	/// <exception cref="PlatformNotSupportedException">This operating system has no serial ports.</exception>
	public static SerialPortLink Open(SerialOpenRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!SerialPlatform.IsSupported)
		{
			throw new PlatformNotSupportedException(SerialPlatform.Unsupported);
		}

		SerialPort port = new(
			request.PortName,
			request.Line.BaudRate,
			MapParity(request.Line.Parity),
			request.Line.DataBits,
			MapStopBits(request.Line.StopBits))
		{
			Handshake = MapHandshake(request.Line.FlowControl),
			ReadTimeout = Milliseconds(request.ReadTimeout),
			WriteTimeout = Milliseconds(request.WriteTimeout),

			// Set before the open, so a board that reboots when DTR rises is not rebooted by connecting to it.
			DtrEnable = request.Dtr,
		};

		bool rts = request.Rts;
		try
		{
			if (request.Line.CanSetRts)
			{
				port.RtsEnable = rts;
			}
			else
			{
				// The driver raises and lowers RTS itself from here on.
				rts = true;
			}

			port.Open();
			return new SerialPortLink(port, request.Line, request.Dtr, rts);
		}
		catch
		{
			port.Dispose();
			throw;
		}
	}

	public SerialSignals ReadSignals()
	{
		// Every driver answers this one, so it is what decides the port is still there. The pins below are not:
		// some virtual and USB drivers refuse the modem status call outright.
		_ = _port.BytesToRead;
		SerialSignals signals = new() { Dtr = _dtr, Rts = _rts, Break = _break };
		if (_pinsUnsupported)
		{
			return signals;
		}

		try
		{
			return signals with
			{
				Cts = _port.CtsHolding,
				Dsr = _port.DsrHolding,
				CarrierDetect = _port.CDHolding,
			};
		}
		catch (NotSupportedException)
		{
			// Asked once and refused: this driver reports no pins, and the panel says so instead of guessing.
			_pinsUnsupported = true;
			return signals;
		}
	}

	public void SetDtr(bool on)
	{
		_port.DtrEnable = on;
		_dtr = on;
	}

	public void SetRts(bool on)
	{
		if (!CanSetRts)
		{
			throw new InvalidOperationException("RTS/CTS flow control drives RTS; turn it off to set the pin by hand.");
		}

		_port.RtsEnable = on;
		_rts = on;
	}

	public void SetBreak(bool on)
	{
		_port.BreakState = on;
		_break = on;
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		// Closing a port whose adapter was unplugged can block inside the driver, and this runs on the way out of a
		// session: past the timeout the close is left to finish on its own rather than holding the window or the circuit.
		try
		{
			await Task.Run(Close).WaitAsync(CloseTimeout);
		}
		catch (TimeoutException)
		{
			// Still inside the driver. Nothing else can be done with the handle from here.
		}
	}

	private static TimeSpan CloseTimeout => TimeSpan.FromSeconds(2);

	private static int Milliseconds(TimeSpan timeout) =>
		timeout <= TimeSpan.Zero ? SerialPort.InfiniteTimeout : (int)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue);

	private static Parity MapParity(SerialParity parity) => parity switch
	{
		SerialParity.Odd => Parity.Odd,
		SerialParity.Even => Parity.Even,
		SerialParity.Mark => Parity.Mark,
		SerialParity.Space => Parity.Space,
		_ => Parity.None,
	};

	private static StopBits MapStopBits(SerialStopBits stopBits) => stopBits switch
	{
		SerialStopBits.OnePointFive => StopBits.OnePointFive,
		SerialStopBits.Two => StopBits.Two,
		_ => StopBits.One,
	};

	private static Handshake MapHandshake(SerialFlowControl flowControl) => flowControl switch
	{
		SerialFlowControl.XOnXOff => Handshake.XOnXOff,
		SerialFlowControl.RtsCts => Handshake.RequestToSend,
		SerialFlowControl.RtsCtsXOnXOff => Handshake.RequestToSendXOnXOff,
		_ => Handshake.None,
	};

	private void Close()
	{
		try
		{
			_port.Dispose();
		}
		catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException or InvalidOperationException)
		{
			// The device is already gone, which is the usual reason a session is closing.
		}
	}
}
