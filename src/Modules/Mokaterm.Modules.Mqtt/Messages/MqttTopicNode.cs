namespace Mokaterm.Modules.Mqtt.Messages;

/// <summary>
/// One level of the topic tree. Nodes are created as messages arrive and never removed while the session lives:
/// a topic that has gone quiet still says what it was, and its kept messages age out on their own.
/// </summary>
internal sealed class MqttTopicNode
{
	private readonly Queue<MqttKeptMessage> _kept = new();

	private MqttTopicNode(MqttTopicNode? parent, string segment, string path)
	{
		Parent = parent;
		Segment = segment;
		Path = path;
	}

	public MqttTopicNode? Parent { get; }

	public string Segment { get; }

	/// <summary>The whole topic up to and including this level. Empty for the root, which is not a topic.</summary>
	public string Path { get; }

	/// <summary>Child levels in name order, which is the order the tree shows unless the view asks for time order.</summary>
	public SortedDictionary<string, MqttTopicNode> Children { get; } = new(StringComparer.Ordinal);

	/// <summary>Messages seen for this exact topic, whether they were kept or aged out.</summary>
	public long Received { get; private set; }

	/// <summary>Messages seen for this topic and everything under it.</summary>
	public long ReceivedWithChildren { get; private set; }

	public bool HasRetained { get; private set; }

	public DateTimeOffset? LastReceivedAt { get; private set; }

	public int KeptCount => _kept.Count;

	/// <summary>The newest kept message for this topic, or null once they have all aged out.</summary>
	public MqttMessage? Newest { get; private set; }

	public static MqttTopicNode CreateRoot() => new(parent: null, segment: "", path: "");

	public MqttTopicNode Child(string segment)
	{
		if (Children.TryGetValue(segment, out MqttTopicNode? existing))
		{
			return existing;
		}

		MqttTopicNode child = new(this, segment, Path.Length == 0 ? segment : Path + '/' + segment);
		Children.Add(segment, child);
		return child;
	}

	public bool HasChild(string segment) => Children.ContainsKey(segment);

	/// <summary>Counts a message against this node and every level above it, kept or not.</summary>
	public void CountReceived(MqttMessage message)
	{
		Received++;
		LastReceivedAt = message.ReceivedAt;
		HasRetained |= message.Retain;
		for (MqttTopicNode? node = this; node is not null; node = node.Parent)
		{
			node.ReceivedWithChildren++;
		}
	}

	public void Keep(MqttKeptMessage kept)
	{
		_kept.Enqueue(kept);
		Newest = kept.Message;
	}

	/// <summary>Takes the oldest kept message off this topic, or null when it holds none.</summary>
	public MqttKeptMessage? Evict()
	{
		if (!_kept.TryDequeue(out MqttKeptMessage? oldest))
		{
			return null;
		}

		if (_kept.Count == 0)
		{
			Newest = null;
		}

		return oldest;
	}

	/// <summary>The kept messages, newest first, at most <paramref name="limit"/> of them.</summary>
	public List<MqttMessage> Snapshot(int limit)
	{
		MqttKeptMessage[] kept = [.. _kept];
		int take = Math.Min(kept.Length, Math.Max(limit, 0));
		List<MqttMessage> messages = new(take);
		for (int index = kept.Length - 1; index >= kept.Length - take; index--)
		{
			messages.Add(kept[index].Message);
		}

		return messages;
	}
}
