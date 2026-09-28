using System.Diagnostics.CodeAnalysis;
using MQTTnet;

namespace Mokaterm.Modules.Mqtt;

/// <summary>
/// The topic rules of MQTT 5 section 4.7, as far as this module needs them. Matching goes through MQTTnet's own
/// comparer so a subscription in the view matches exactly what the broker would match; the checks here are for
/// telling the user what is wrong before anything reaches the wire, because MQTTnet answers a bad filter with a
/// <c>MqttProtocolViolationException</c> from the middle of a subscribe.
/// </summary>
public static class MqttTopicFilters
{
	/// <summary>Longest filter or topic accepted. MQTT allows 65535 bytes; nothing readable comes close.</summary>
	public const int MaxLength = 512;

	/// <summary>
	/// Longest topic the explorer keeps for a message the broker sent. Looser than <see cref="MaxLength"/>, which
	/// is what a person may type, because a machine-generated topic is allowed to be ugly.
	/// </summary>
	public const int MaxReceivedLength = 1024;

	/// <summary>
	/// Levels a topic may have. The topic tree is walked recursively, and a broker could otherwise publish a topic
	/// deep enough to overflow the stack, which no <c>catch</c> can save.
	/// </summary>
	public const int MaxLevels = 64;

	/// <summary>Matches every topic that does not start with <c>$</c>.</summary>
	public const string Everything = "#";

	/// <summary>True when <paramref name="topic"/> is a name a message may be published to.</summary>
	public static bool IsValidTopic([NotNullWhen(true)] string? topic) => ValidateTopic(topic) is null;

	/// <summary>Null when the topic can be published to, otherwise why it cannot, written for the user.</summary>
	public static string? ValidateTopic(string? topic)
	{
		if (string.IsNullOrEmpty(topic))
		{
			return "A topic cannot be empty.";
		}

		if (topic.Length > MaxLength)
		{
			return $"A topic is limited to {MaxLength} characters here.";
		}

		if (topic.Contains('\0', StringComparison.Ordinal))
		{
			return "A topic cannot contain a null character.";
		}

		if (topic.Contains(MqttTopicFilterComparer.MultiLevelWildcard, StringComparison.Ordinal)
			|| topic.Contains(MqttTopicFilterComparer.SingleLevelWildcard, StringComparison.Ordinal))
		{
			return "A published topic cannot contain the wildcards + or #.";
		}

		return null;
	}

	/// <summary>True when <paramref name="filter"/> is a filter a subscription may carry.</summary>
	public static bool IsValidFilter([NotNullWhen(true)] string? filter) => ValidateFilter(filter) is null;

	/// <summary>Null when the filter can be subscribed to, otherwise why it cannot, written for the user.</summary>
	public static string? ValidateFilter(string? filter)
	{
		if (string.IsNullOrEmpty(filter))
		{
			return "A topic filter cannot be empty.";
		}

		if (filter.Length > MaxLength)
		{
			return $"A topic filter is limited to {MaxLength} characters here.";
		}

		if (filter.Contains('\0', StringComparison.Ordinal))
		{
			return "A topic filter cannot contain a null character.";
		}

		foreach (string level in filter.Split(MqttTopicFilterComparer.LevelSeparator))
		{
			if (level.Length > 1
				&& (level.Contains(MqttTopicFilterComparer.MultiLevelWildcard, StringComparison.Ordinal)
					|| level.Contains(MqttTopicFilterComparer.SingleLevelWildcard, StringComparison.Ordinal)))
			{
				return "A wildcard takes a whole level: sensors/+/temp, never sensors/a+/temp.";
			}
		}

		int multiLevel = filter.IndexOf(MqttTopicFilterComparer.MultiLevelWildcard, StringComparison.Ordinal);
		if (multiLevel >= 0 && multiLevel != filter.Length - 1)
		{
			return "# matches the rest of a topic, so it can only be the last level.";
		}

		return null;
	}

	/// <summary>True when a message on <paramref name="topic"/> reaches a subscription to <paramref name="filter"/>.</summary>
	public static bool Matches(string? topic, string? filter) =>
		topic is not null
		&& filter is not null
		&& MqttTopicFilterComparer.Compare(topic, filter) == MqttTopicFilterCompareResult.IsMatch;

	/// <summary>
	/// The levels of a topic, in order. An empty level is real in MQTT (<c>a//b</c> has three), so nothing is
	/// dropped here.
	/// </summary>
	public static string[] Split(string topic)
	{
		ArgumentNullException.ThrowIfNull(topic);
		return topic.Split(MqttTopicFilterComparer.LevelSeparator);
	}

	/// <summary>
	/// True when a topic the broker sent is short and shallow enough to put in the tree. A topic that is not is
	/// counted as dropped rather than shown, because the alternative is an unbounded tree and a recursive walk
	/// over it.
	/// </summary>
	public static bool IsWithinLimits(string? topic) =>
		topic is { Length: > 0 and <= MaxReceivedLength } && CountLevels(topic) <= MaxLevels;

	private static int CountLevels(string topic)
	{
		int levels = 1;
		foreach (char character in topic)
		{
			if (character == MqttTopicFilterComparer.LevelSeparator)
			{
				levels++;
			}
		}

		return levels;
	}

	/// <summary>
	/// True for the topics a broker keeps to itself, such as <c>$SYS/broker/uptime</c>. A subscription to
	/// <c>#</c> never reaches them, which is why the view offers one to <c>$SYS/#</c> separately.
	/// </summary>
	public static bool IsReserved(string? topic) =>
		topic is { Length: > 0 } && topic[0] == MqttTopicFilterComparer.ReservedTopicPrefix;
}
