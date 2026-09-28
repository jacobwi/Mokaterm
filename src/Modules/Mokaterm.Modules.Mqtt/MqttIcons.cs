using Moka.Red.Core.Icons;

namespace Mokaterm.Modules.Mqtt;

/// <summary>
/// Icons this module needs, drawn like the ones in <c>MokatermIcons</c>: stroked paths on a 24 by 24 grid. A broker
/// is neither a terminal nor a screen, so it gets its own.
/// </summary>
public static class MqttIcons
{
	/// <summary>A hub with three links leaving it: what a broker does, for sessions and for the settings page.</summary>
	public static readonly MokaIconDefinition Broker = new("mt-mqtt",
		"M12 9.5a2.5 2.5 0 1 1 0 5 2.5 2.5 0 0 1 0-5z M12 9.5V4 M10 6l2-2 2 2 M10.1 13.4L5.6 16 M5.1 13.4L5.6 16l2.8.3 M13.9 13.4l4.5 2.6 M18.9 13.4L18.4 16l-2.8.3");

	/// <summary>A topic in a tree: a small node with a stub to its parent.</summary>
	public static readonly MokaIconDefinition Topic = new("mt-mqtt-topic",
		"M5 4v12a2 2 0 0 0 2 2h4 M11 15h8a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1h-8a1 1 0 0 1-1-1v-3a1 1 0 0 1 1-1z M11 4h8a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1h-8a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z");

	/// <summary>An arrow leaving a box: the publish form.</summary>
	public static readonly MokaIconDefinition Publish = new("mt-mqtt-publish",
		"M4 5h10a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z M11 12h10 M18 9l3 3-3 3");
}
