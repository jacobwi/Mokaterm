using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Mokaterm.Modules.Mqtt.Components;

/// <summary>The subscriptions saved with a connection, edited in place inside the connection editor.</summary>
public partial class MqttSubscriptionList : ComponentBase
{
	/// <summary>
	/// What a broker publishes about itself. MQTT reserves the <c>$</c> prefix and a subscription to <c>#</c> never
	/// matches it, which is why this needs a filter of its own.
	/// </summary>
	internal const string BrokerTopics = "$SYS/#";

	private Guid? _editing;

	[Parameter, EditorRequired]
	public IReadOnlyList<MqttSubscription> Subscriptions { get; set; } = [];

	[Parameter]
	public EventCallback<IReadOnlyList<MqttSubscription>> SubscriptionsChanged { get; set; }

	[Parameter]
	public bool Disabled { get; set; }

	private bool IsFull => Subscriptions.Count >= MqttConnectionOptions.MaxSubscriptions;

	/// <summary>The QoS as a broker's own tools write it: <c>QOS 0</c>.</summary>
	internal static string QosLabel(MqttQos qos) =>
		string.Create(CultureInfo.InvariantCulture, $"QOS {(int)qos}");

	private Task AddAsync(string filter)
	{
		if (IsFull)
		{
			return Task.CompletedTask;
		}

		MqttSubscription subscription = MqttSubscription.Create() with { TopicFilter = filter };

		// Opened straight away, because a filter is the one field nobody wants the default of.
		_editing = subscription.Id;
		return SubscriptionsChanged.InvokeAsync([.. Subscriptions, subscription]);
	}

	private Task ReplaceAsync(MqttSubscription subscription) =>
		SubscriptionsChanged.InvokeAsync([.. Subscriptions.Select(existing => existing.Id == subscription.Id ? subscription : existing)]);

	private Task RemoveAsync(Guid subscriptionId)
	{
		if (_editing == subscriptionId)
		{
			_editing = null;
		}

		return SubscriptionsChanged.InvokeAsync([.. Subscriptions.Where(existing => existing.Id != subscriptionId)]);
	}

	private void Toggle(Guid subscriptionId) => _editing = _editing == subscriptionId ? null : subscriptionId;
}
