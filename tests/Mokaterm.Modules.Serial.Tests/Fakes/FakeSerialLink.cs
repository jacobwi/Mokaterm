using Mokaterm.Modules.Serial.Protocol;

namespace Mokaterm.Modules.Serial.Tests.Fakes;

/// <summary>An open port that is not one: the channel's whole view of a device, backed by a stream a test drives.</summary>
internal sealed class FakeSerialLink : ISerialLink
{
	private SerialSignals _signals = new() { Dtr = true, Rts = true, Cts = true, Dsr = true };

	public FakeSerialLink(SerialLineSettings? line = null, string portName = "COM-TEST")
	{
		Line = line ?? SerialLineSettings.Default;
		PortName = portName;
	}

	public string PortName { get; }

	public SerialLineSettings Line { get; }

	public FakeSerialStream Device { get; } = new();

	public Stream Stream => Device;

	public bool CanSetRts { get; set; } = true;

	public bool ReportsPins { get; set; } = true;

	/// <summary>Set to make reading the pins fail, which is what an unplugged adapter does.</summary>
	public Exception? SignalFailure { get; set; }

	public int SignalReads { get; private set; }

	public int DisposeCount { get; private set; }

	public SerialSignals ReadSignals()
	{
		SignalReads++;
		return SignalFailure is { } failure ? throw failure : _signals;
	}

	public void SetDtr(bool on) => _signals = _signals with { Dtr = on };

	public void SetRts(bool on)
	{
		if (!CanSetRts)
		{
			throw new InvalidOperationException("RTS/CTS flow control drives RTS.");
		}

		_signals = _signals with { Rts = on };
	}

	public void SetBreak(bool on) => _signals = _signals with { Break = on };

	public ValueTask DisposeAsync()
	{
		DisposeCount++;

		// Closing a real port is what lets go of a read that is waiting in the driver.
		Device.CloseLine();
		return ValueTask.CompletedTask;
	}
}
