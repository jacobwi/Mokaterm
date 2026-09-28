namespace Mokaterm.Modules.Mqtt;

/// <summary>
/// What came back from a publish. A failure lands here rather than in an exception, because a refused publish is
/// something the form shows next to its button, not something that ends a session.
/// </summary>
public sealed record MqttPublishOutcome
{
	public required bool IsSuccess { get; init; }

	/// <summary>The broker's reason, or the local failure, written for the user. Null when it worked.</summary>
	public string? Error { get; init; }

	/// <summary>
	/// True when the broker took the message but told us nobody was subscribed to the topic, which is the usual
	/// sign of a typo in a topic.
	/// </summary>
	public bool NoMatchingSubscribers { get; init; }

	public static MqttPublishOutcome Success(bool noMatchingSubscribers = false) =>
		new() { IsSuccess = true, NoMatchingSubscribers = noMatchingSubscribers };

	public static MqttPublishOutcome Failed(string error) => new() { IsSuccess = false, Error = error };
}
