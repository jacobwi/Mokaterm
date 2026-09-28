using System.Globalization;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Mqtt.Components;

/// <summary>
/// The MQTT settings page: how much of a broker's traffic one session keeps, and what a publish starts at. Values
/// outside the supported range are clamped when saved.
/// </summary>
public partial class MqttSettingsPage : SettingsSectionBase<MqttSettings>
{
	private static readonly IReadOnlyList<EnumOption<MqttQos>> QosOptions =
	[
		new(MqttQos.AtMostOnce, "0"),
		new(MqttQos.AtLeastOnce, "1"),
		new(MqttQos.ExactlyOnce, "2"),
	];

	/// <summary>The worst case these caps allow, so the numbers above mean something.</summary>
	private string MemoryEstimate
	{
		get
		{
			MqttSettings settings = Settings.Clamped();
			long payload = settings.TotalPayloadKilobytes * 1024L;
			return string.Create(
				CultureInfo.CurrentCulture,
				$"At most, one session holds {settings.TotalMessages} messages, {DisplayFormat.Bytes(payload)} of payload and {settings.MaxTopicCount} topics. Changing any of this leaves open sessions alone: the caps are read when a session connects.");
		}
	}

	private Task SaveAsync(Func<MqttSettings, MqttSettings> change) => UpdateAsync(settings => change(settings).Clamped());
}
