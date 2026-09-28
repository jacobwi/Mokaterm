using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Serial.Protocol;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Serial.Components;

/// <summary>
/// The serial part of the connection editor: which port, the line it runs at, what Enter sends, who echoes, and the
/// two pins this end drives. The ports of this machine are offered by name, with a plain field for one that is not
/// plugged in yet.
/// </summary>
public partial class SerialOptionsEditor : ConnectionOptionsEditorBase<SerialConnectionOptions>
{
	/// <summary>The empty choice in the port list: open whatever the host's address names.</summary>
	private const string HostAddressChoice = "";

	private const string PortHelp = "Empty opens the port named by the host's address, which is the usual way.";

	private IReadOnlyList<string> _detected = [];
	private IReadOnlyList<string> _portChoices = [HostAddressChoice];
	private IReadOnlyList<int> _baudRates = SerialLineSettings.CommonBaudRates;
	private string _portText = "";
	private string? _portError;
	private bool _typingPort;
	private bool _seeded;

	[Inject]
	private ISettingsService SettingsService { get; set; } = default!;

	private string PortPlaceholder => _typingPort ? "COM3 or /dev/ttyUSB0" : "The host's address";

	private string DataBitsValue => Current.Line.DataBits.ToString(CultureInfo.InvariantCulture);

	/// <summary>RTS is shown as asserted while the driver owns it, because that is what the driver does with it.</summary>
	private bool RtsValue => !Current.Line.CanSetRts || Current.Rts;

	private string RtsHelp => Current.Line.CanSetRts
		? "Leave on unless the device wants RTS low. Some adapters wire it to reset or to a boot pin."
		: "RTS/CTS flow control drives RTS, so it cannot be set here.";

	private string FlowControlHelp => Current.Line.FlowControl switch
	{
		SerialFlowControl.XOnXOff => "The device pauses with XOFF, which means 0x11 and 0x13 stop being data.",
		SerialFlowControl.RtsCts => "The device pauses by lowering CTS. Needs a cable that carries RTS and CTS.",
		SerialFlowControl.RtsCtsXOnXOff => "Both at once, for hardware that asks for it.",
		_ => "What a console cable normally uses: nothing holds the line back.",
	};

	private string LineEndingHelp => Current.LineEnding switch
	{
		SerialLineEnding.CrLf => "CR LF, for a device that wants a full new line.",
		SerialLineEnding.Lf => "LF alone, which a Linux console on a serial port takes as Enter.",
		_ => "CR alone, which is what bootloaders and switch consoles expect.",
	};

	protected override SerialConnectionOptions From(ProtocolOptions options) => SerialConnectionOptions.From(options);

	protected override ProtocolOptions ApplyTo(SerialConnectionOptions current, ProtocolOptions options) => current.ApplyTo(options);

	/// <summary>The port and baud lists always show what this connection has, whatever this machine happens to offer.</summary>
	protected override void OnCurrentChanged() => SyncChoices();

	protected override void OnInitialized() => RefreshPorts();

	protected override async Task OnParametersSetAsync()
	{
		// A connection that carries no serial key at all is new: it starts from the defaults in Settings rather than
		// from whatever this record happens to default to.
		if (!_seeded)
		{
			_seeded = true;
			if (!SerialConnectionOptions.HasAny(Options))
			{
				await ChangeAsync(SettingsService.Get<SerialSettings>().Clamped().NewConnectionOptions);
				return;
			}
		}

		if (_portError is null)
		{
			_portText = Current.PortName ?? HostAddressChoice;
			if (IsUnlisted(_portText))
			{
				// A saved port this machine does not have cannot be picked from a list, so it is shown as text.
				_typingPort = true;
			}
		}

		SyncChoices();
	}

	private static string PortLabel(string port) => port.Length == 0 ? "The host's address" : port;

	private void RefreshPorts()
	{
		_detected = SerialPortNames.List();
		SyncChoices();

		// Nothing plugged in, or a name this machine does not have: neither can be picked from a list.
		_typingPort = _detected.Count == 0 || IsUnlisted(_portText);
	}

	/// <summary>True for a port name this machine does not have right now.</summary>
	private bool IsUnlisted(string port) =>
		port.Length > 0 && !_detected.Contains(port, StringComparer.OrdinalIgnoreCase);

	/// <summary>Keeps the lists showing what this connection already has, even when this machine has no such port.</summary>
	private void SyncChoices()
	{
		_portChoices = IsUnlisted(_portText)
			? [HostAddressChoice, .. _detected, _portText]
			: [HostAddressChoice, .. _detected];

		int baudRate = Current.Line.BaudRate;
		_baudRates = SerialLineSettings.CommonBaudRates.Contains(baudRate)
			? SerialLineSettings.CommonBaudRates
			: [.. SerialLineSettings.CommonBaudRates.Append(baudRate).Order()];
	}

	private void TogglePortEntry()
	{
		_typingPort = !_typingPort;
		SyncChoices();
	}

	private Task OnPortChangedAsync(string? value)
	{
		_portText = SerialPortNames.Normalize(value);
		if (_portText.Length == 0)
		{
			_portError = null;
			return ChangeAsync(Current with { PortName = null });
		}

		if (SerialPortNames.Validate(_portText) is { } invalid)
		{
			_portError = invalid;
			return Task.CompletedTask;
		}

		_portError = null;
		return ChangeAsync(Current with { PortName = _portText });
	}

	private Task OnEncodingChangedAsync(string value) => ChangeAsync(Current with { EncodingName = value });

	private Task OnBaudRateChangedAsync(int value) =>
		value is >= SerialLineSettings.MinBaudRate and <= SerialLineSettings.MaxBaudRate
			? UpdateLineAsync(Current.Line with { BaudRate = value })
			: Task.CompletedTask;

	private Task OnDataBitsChangedAsync(string value) =>
		int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int dataBits)
		&& dataBits is >= SerialLineSettings.MinDataBits and <= SerialLineSettings.MaxDataBits
			? UpdateLineAsync(Current.Line with { DataBits = dataBits })
			: Task.CompletedTask;

	private Task OnParityChangedAsync(string value) =>
		Enum.TryParse(value, out SerialParity parity) && Enum.IsDefined(parity)
			? UpdateLineAsync(Current.Line with { Parity = parity })
			: Task.CompletedTask;

	private Task OnStopBitsChangedAsync(string value) =>
		Enum.TryParse(value, out SerialStopBits stopBits) && Enum.IsDefined(stopBits)
			? UpdateLineAsync(Current.Line with { StopBits = stopBits })
			: Task.CompletedTask;

	private Task OnFlowControlChangedAsync(string value) =>
		Enum.TryParse(value, out SerialFlowControl flowControl) && Enum.IsDefined(flowControl)
			? UpdateLineAsync(Current.Line with { FlowControl = flowControl })
			: Task.CompletedTask;

	private Task OnLineEndingChangedAsync(string value) =>
		Enum.TryParse(value, out SerialLineEnding lineEnding) && Enum.IsDefined(lineEnding)
			? ChangeAsync(Current with { LineEnding = lineEnding })
			: Task.CompletedTask;

	private Task OnLocalEchoChangedAsync(bool value) => ChangeAsync(Current with { LocalEcho = value });

	private Task OnDtrChangedAsync(bool value) => ChangeAsync(Current with { Dtr = value });

	private Task OnRtsChangedAsync(bool value) => ChangeAsync(Current with { Rts = value });

	private Task UpdateLineAsync(SerialLineSettings line) => ChangeAsync(Current with { Line = line });
}
