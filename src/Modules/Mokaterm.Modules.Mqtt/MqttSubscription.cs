using System.Globalization;

namespace Mokaterm.Modules.Mqtt;

/// <summary>
/// One topic filter a session subscribes to, saved with a connection or added for a single session. Stored as one
/// <c>mqtt.sub.NN</c> option per subscription so a saved connection still loads after this record grows a field.
/// </summary>
public sealed record MqttSubscription
{
	private const char Separator = '|';

	private const int FieldCount = 4;

	/// <summary>Stable across edits, so a live subscription keeps its row when the connection is saved again.</summary>
	public required Guid Id { get; init; }

	public string TopicFilter { get; init; } = "";

	public MqttQos QualityOfService { get; init; }

	/// <summary>Subscribed as soon as the session connects. Off leaves it to the session view.</summary>
	public bool SubscribeWithSession { get; init; } = true;

	/// <summary>A new subscription to everything, which is what an explorer starts with.</summary>
	public static MqttSubscription Create() => new() { Id = Guid.NewGuid(), TopicFilter = MqttTopicFilters.Everything };

	/// <summary>Reads one stored value. Null when the value is not a subscription this version understands.</summary>
	public static MqttSubscription? Parse(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		// The filter is last and unsplit, because a topic filter may contain the separator.
		string[] fields = value.Split(Separator, FieldCount);
		if (fields.Length < FieldCount
			|| !Guid.TryParse(fields[0], CultureInfo.InvariantCulture, out Guid id)
			|| !Enum.TryParse(fields[1], ignoreCase: true, out MqttQos qos)
			|| !Enum.IsDefined(qos))
		{
			return null;
		}

		MqttSubscription subscription = new()
		{
			Id = id,
			QualityOfService = qos,
			SubscribeWithSession = !string.Equals(fields[2], "manual", StringComparison.OrdinalIgnoreCase),
			TopicFilter = fields[3],
		};

		return subscription.Validate() is null ? subscription : null;
	}

	/// <summary>The stored form. <see cref="Parse"/> reads it back.</summary>
	public string Format() => string.Join(
		Separator,
		Id.ToString("D", CultureInfo.InvariantCulture),
		QualityOfService.ToString(),
		SubscribeWithSession ? "auto" : "manual",
		TopicFilter);

	/// <summary>Null when the subscription can be sent to a broker, otherwise why it cannot.</summary>
	public string? Validate() => MqttTopicFilters.ValidateFilter(TopicFilter);
}
