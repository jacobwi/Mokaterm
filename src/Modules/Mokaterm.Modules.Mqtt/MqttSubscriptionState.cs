namespace Mokaterm.Modules.Mqtt;

public enum MqttSubscriptionState
{
	/// <summary>Saved with the connection but not sent to the broker, because it is not opened with the session.</summary>
	Idle,

	/// <summary>The SUBSCRIBE is on its way.</summary>
	Subscribing,

	/// <summary>The broker granted it. Messages matching the filter arrive from here on.</summary>
	Active,

	/// <summary>The broker refused it, or the connection failed while it was being sent.</summary>
	Failed,
}
