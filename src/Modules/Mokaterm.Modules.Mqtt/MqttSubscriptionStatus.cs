using System.Globalization;

namespace Mokaterm.Modules.Mqtt;

/// <summary>One subscription as the session sees it right now.</summary>
public sealed record MqttSubscriptionStatus
{
	public required MqttSubscription Subscription { get; init; }

	public required MqttSubscriptionState State { get; init; }

	/// <summary>The QoS the broker granted, which may be lower than the one asked for.</summary>
	public MqttQos? GrantedQos { get; init; }

	/// <summary>Why it failed, written for the user. Null unless <see cref="State"/> is Failed.</summary>
	public string? Error { get; init; }

	/// <summary>True for a subscription added from the view, which is not saved with the connection.</summary>
	public bool IsSessionOnly { get; init; }

	/// <summary>Messages this subscription's filter has matched since it went active.</summary>
	public long Matched { get; init; }

	/// <summary>What the row shows beside the filter.</summary>
	public string Describe() => State switch
	{
		MqttSubscriptionState.Active => GrantedQos is { } granted && granted != Subscription.QualityOfService
			? string.Create(CultureInfo.CurrentCulture, $"granted QoS {(int)granted}, asked for {(int)Subscription.QualityOfService}")
			: string.Create(CultureInfo.CurrentCulture, $"QoS {(int)Subscription.QualityOfService}"),
		MqttSubscriptionState.Subscribing => "subscribing",
		MqttSubscriptionState.Failed => Error ?? "the broker refused it",
		_ => "not subscribed",
	};
}
