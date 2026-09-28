namespace Mokaterm.Modules.Mqtt;

/// <summary>
/// The session feature behind the MQTT explorer, the way <c>ITerminalChannel</c> is the one behind the terminal.
/// Reached through <c>IProtocolSession.GetFeature&lt;IMqttConnection&gt;()</c>.
/// </summary>
/// <remarks>
/// There is no event per message on purpose. A broker can publish thousands a second, and a handler that redraws
/// would turn each one into a render; <see cref="Messages"/> keeps them under its own caps and carries a version
/// the view compares on a timer.
/// </remarks>
public interface IMqttConnection
{
	/// <summary>Raised after the state or the subscriptions changed. Handlers may run on any thread.</summary>
	event Action? Changed;

	MqttConnectionInfo Info { get; }

	MqttConnectionState State { get; }

	/// <summary>Why the connection closed, when it closed by itself. Null while it is up.</summary>
	string? CloseReason { get; }

	/// <summary>Everything the session kept of what the broker sent.</summary>
	MqttMessageStore Messages { get; }

	/// <summary>Every subscription this session knows, the ones saved with the connection first.</summary>
	IReadOnlyList<MqttSubscriptionStatus> Subscriptions { get; }

	/// <summary>
	/// Subscribes, or subscribes again after a failure. A subscription the connection does not carry is added for
	/// this session only. A broker that refuses the filter lands in the returned status, not in an exception.
	/// </summary>
	Task<MqttSubscriptionStatus> SubscribeAsync(MqttSubscription subscription, CancellationToken cancellationToken = default);

	/// <summary>
	/// Unsubscribes and drops the row. A subscription saved with the connection comes back on the next connect,
	/// which is what makes this safe to offer for both kinds.
	/// </summary>
	Task RemoveSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

	/// <summary>Publishes one message. A refusal comes back in the outcome.</summary>
	Task<MqttPublishOutcome> PublishAsync(MqttPublishRequest request, CancellationToken cancellationToken = default);
}
