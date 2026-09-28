using Microsoft.AspNetCore.Components;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Mqtt.Components;

/// <summary>
/// The fields of one subscription, shared by the connection editor and the explorer's own subscription panel. It
/// only reports a new subscription; whoever owns it decides what to do with it.
/// </summary>
public partial class MqttSubscriptionForm : ComponentBase
{
	private readonly FieldDraft<string> _filter = new();

	[Parameter, EditorRequired]
	public MqttSubscription Subscription { get; set; } = MqttSubscription.Create();

	[Parameter]
	public EventCallback<MqttSubscription> SubscriptionChanged { get; set; }

	/// <summary>False in a live session, where a subscription is sent right away rather than marked to open later.</summary>
	[Parameter]
	public bool ShowSubscribeWithSession { get; set; } = true;

	[Parameter]
	public bool Disabled { get; set; }

	private static string FilterHelp =>
		"+ takes one level, # the rest: sensors/+/temp, sensors/#. A filter never reaches the broker's own $SYS topics unless it names them.";

	private string QosHelp => Subscription.QualityOfService switch
	{
		MqttQos.AtLeastOnce => "1: the broker repeats until this client acknowledges, so a message may arrive twice.",
		MqttQos.ExactlyOnce => "2: one delivery, at the cost of four packets per message.",
		_ => "0: whatever arrives, arrives. What an explorer usually wants.",
	};

	private Task SetFilterAsync(string value)
	{
		if (MqttTopicFilters.ValidateFilter(value) is { } error)
		{
			_filter.Reject(value, error);
			return Task.CompletedTask;
		}

		_filter.Clear();
		return ChangeAsync(Subscription with { TopicFilter = value });
	}

	private Task SetQosAsync(string value) =>
		Enum.TryParse(value, out MqttQos qos) && Enum.IsDefined(qos)
			? ChangeAsync(Subscription with { QualityOfService = qos })
			: Task.CompletedTask;

	private Task SetSubscribeWithSessionAsync(bool value) =>
		ChangeAsync(Subscription with { SubscribeWithSession = value });

	private Task ChangeAsync(MqttSubscription subscription) => SubscriptionChanged.InvokeAsync(subscription);
}
