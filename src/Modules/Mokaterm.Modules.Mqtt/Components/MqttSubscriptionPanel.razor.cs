using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.Core.Enums;

namespace Mokaterm.Modules.Mqtt.Components;

/// <summary>
/// The subscriptions of the live session: what the connection carries, plus anything added here for this session
/// alone. A filter that failed keeps its row with the reason on it and a button to try again.
/// </summary>
public sealed partial class MqttSubscriptionPanel : ComponentBase
{
	private MqttSubscription? _draft;
	private bool _busy;

	[Parameter, EditorRequired]
	public IMqttConnection Connection { get; set; } = default!;

	[Parameter, EditorRequired]
	public IReadOnlyList<MqttSubscriptionStatus> Subscriptions { get; set; } = [];

	/// <summary>What a new filter starts its QoS at, from the settings.</summary>
	[Parameter]
	public MqttQos DefaultQos { get; set; }

	private string CountLabel
	{
		get
		{
			int active = Subscriptions.Count(status => status.State == MqttSubscriptionState.Active);
			return string.Create(CultureInfo.CurrentCulture, $"{active} of {Subscriptions.Count} subscribed");
		}
	}

	private static MokaColor StateColor(MqttSubscriptionState state) => state switch
	{
		MqttSubscriptionState.Active => MokaColor.Success,
		MqttSubscriptionState.Subscribing => MokaColor.Warning,
		MqttSubscriptionState.Failed => MokaColor.Error,
		_ => MokaColor.Surface,
	};

	private static string DescribeStyle(MqttSubscriptionStatus status) => status.State == MqttSubscriptionState.Failed
		? "color:var(--moka-color-error)"
		: "color:var(--moka-color-on-surface-variant)";

	private static string RemoveLabel(MqttSubscriptionStatus status) => status.IsSessionOnly
		? "Remove the subscription"
		: "Unsubscribe until the next connect";

	private void ToggleDraft() =>
		_draft = _draft is null ? MqttSubscription.Create() with { QualityOfService = DefaultQos } : null;

	private void OnDraftChanged(MqttSubscription subscription) => _draft = subscription;

	private async Task AddAsync()
	{
		if (_draft is not { } draft || draft.Validate() is not null)
		{
			return;
		}

		_busy = true;
		try
		{
			MqttSubscriptionStatus status = await Connection.SubscribeAsync(draft);

			// A refused filter keeps the draft open so it can be corrected; a granted one closes it.
			_draft = status.State == MqttSubscriptionState.Failed ? draft : null;
		}
		finally
		{
			_busy = false;
		}
	}

	private async Task SubscribeAsync(MqttSubscription subscription)
	{
		_busy = true;
		try
		{
			await Connection.SubscribeAsync(subscription);
		}
		finally
		{
			_busy = false;
		}
	}

	private async Task RemoveAsync(Guid subscriptionId)
	{
		_busy = true;
		try
		{
			await Connection.RemoveSubscriptionAsync(subscriptionId);
		}
		finally
		{
			_busy = false;
		}
	}
}
