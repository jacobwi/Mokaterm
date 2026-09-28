using System.Globalization;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Serial.Components;

/// <summary>
/// Settings page for <see cref="SerialSettings"/>: the line a new connection starts from and the timeouts every port
/// is opened with. Values outside the supported range, and a frame no UART can do, are corrected when saved.
/// </summary>
public partial class SerialSettingsPage : SettingsSectionBase<SerialSettings>
{
	private static readonly IReadOnlyList<EnumOption<SerialParity>> ParityOptions =
	[
		new(SerialParity.None, "None"),
		new(SerialParity.Odd, "Odd"),
		new(SerialParity.Even, "Even"),
		new(SerialParity.Mark, "Mark"),
		new(SerialParity.Space, "Space"),
	];

	private static readonly IReadOnlyList<EnumOption<SerialStopBits>> StopBitsOptions =
	[
		new(SerialStopBits.One, "1"),
		new(SerialStopBits.OnePointFive, "1.5"),
		new(SerialStopBits.Two, "2"),
	];

	private static readonly IReadOnlyList<EnumOption<SerialFlowControl>> FlowControlOptions =
	[
		new(SerialFlowControl.None, "None"),
		new(SerialFlowControl.XOnXOff, "XON/XOFF"),
		new(SerialFlowControl.RtsCts, "RTS/CTS"),
		new(SerialFlowControl.RtsCtsXOnXOff, "Both"),
	];

	private static readonly IReadOnlyList<EnumOption<SerialLineEnding>> LineEndingOptions =
	[
		new(SerialLineEnding.Cr, "CR"),
		new(SerialLineEnding.CrLf, "CR LF"),
		new(SerialLineEnding.Lf, "LF"),
	];

	private string DataBitsValue => Settings.DataBits.ToString(CultureInfo.InvariantCulture);

	/// <summary>The whole line as a device sees it, corrected to a frame a UART can do.</summary>
	private string FrameDescription => Settings.Clamped().Line.Describe();

	// Data bits are a plain number rather than an enum, so the segments carry their own strings.
	private Task OnDataBitsChangedAsync(string value) =>
		int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int dataBits)
			? SaveAsync(settings => settings with { DataBits = dataBits })
			: Task.CompletedTask;

	private Task SaveAsync(Func<SerialSettings, SerialSettings> change) => UpdateAsync(settings => change(settings).Clamped());
}
