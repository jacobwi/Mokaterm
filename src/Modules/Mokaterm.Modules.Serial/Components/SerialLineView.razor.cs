using System.Globalization;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Modules.Serial.Protocol;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Serial.Components;

/// <summary>
/// The live line of one serial session: what it is running at, the control pins, the two this end drives, and a
/// break. Anything that belongs to the connection rather than to this session is edited in the connection.
/// </summary>
public sealed partial class SerialLineView : SessionViewBase, IDisposable
{
	private ISessionHandle? _handle;
	private IProtocolSession? _attached;
	private ISerialPortFeature? _feature;
	private SerialPortStatus? _status;
	private string? _error;
	private bool _busy;

	public void Dispose()
	{
		Detach();
		if (_handle is { } handle)
		{
			handle.Changed -= OnSessionChanged;
			_handle = null;
		}
	}

	protected override void OnParametersSet()
	{
		if (!ReferenceEquals(_handle, Session))
		{
			if (_handle is { } previous)
			{
				previous.Changed -= OnSessionChanged;
			}

			_handle = Session;
			_handle.Changed += OnSessionChanged;
		}

		Attach();
	}

	private static string FlowControlLabel(SerialFlowControl flowControl) => flowControl switch
	{
		SerialFlowControl.XOnXOff => "XON/XOFF",
		SerialFlowControl.RtsCts => "RTS/CTS",
		SerialFlowControl.RtsCtsXOnXOff => "RTS/CTS + XON/XOFF",
		_ => "no flow control",
	};

	private static string LineEndingLabel(SerialLineEnding lineEnding) => lineEnding switch
	{
		SerialLineEnding.CrLf => "CR LF",
		SerialLineEnding.Lf => "LF",
		_ => "CR",
	};

	/// <summary>How this session treats what is typed, which is the other half of "why does it look wrong".</summary>
	private static string InputSummary(SerialPortStatus status) =>
		$"Enter sends {LineEndingLabel(status.LineEnding)} | {(status.LocalEcho ? "typing is shown here" : "the device echoes")} | {status.EncodingName}";

	private static string WriteTimeoutText(SerialPortStatus status) => string.Create(
		CultureInfo.CurrentCulture,
		$"{status.WriteTimeouts} write(s) were not taken in time. The device is holding the line, or has stopped listening.");

	private static string RtsHelp(SerialPortStatus status) => status.CanSetRts
		? "Some adapters wire RTS to reset or to a boot pin."
		: "RTS/CTS flow control drives RTS, so it cannot be set here.";

	private static (string Name, bool High)[] Pins(SerialPortStatus status) =>
	[
		("CTS", status.Signals.Cts),
		("DSR", status.Signals.Dsr),
		("CD", status.Signals.CarrierDetect),
		("BREAK", status.Signals.Break),
	];

	private void Attach()
	{
		IProtocolSession? session = Session.Session;
		if (ReferenceEquals(session, _attached))
		{
			return;
		}

		Detach();
		_attached = session;
		_feature = session?.GetFeature<ISerialPortFeature>();
		if (_feature is { } feature)
		{
			feature.Changed += OnPortChanged;
			_status = feature.Status;
		}
		else
		{
			_status = null;
		}
	}

	private void Detach()
	{
		if (_feature is { } feature)
		{
			feature.Changed -= OnPortChanged;
		}

		_feature = null;
		_attached = null;
	}

	// The channel raises this from the loop that reads the pins, which is not this thread.
	private void OnPortChanged() => _ = InvokeAsync(() =>
	{
		_status = _feature?.Status;
		StateHasChanged();
	});

	private void OnSessionChanged() => _ = InvokeAsync(() =>
	{
		Attach();
		StateHasChanged();
	});

	private Task SetDtrAsync(bool on) => RunAsync(feature => feature.SetDtrAsync(on));

	private Task SetRtsAsync(bool on) => RunAsync(feature => feature.SetRtsAsync(on));

	private Task SendBreakAsync() => RunAsync(feature => feature.SendBreakAsync());

	private Task RefreshAsync() => RunAsync(feature => feature.RefreshAsync());

	private async Task RunAsync(Func<ISerialPortFeature, Task> action)
	{
		if (_feature is not { } feature)
		{
			return;
		}

		_busy = true;
		_error = null;
		try
		{
			await action(feature);
		}
		catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException or UnauthorizedAccessException)
		{
			_error = ex.Message;
		}
		finally
		{
			_busy = false;
			_status = feature.Status;
		}
	}
}
