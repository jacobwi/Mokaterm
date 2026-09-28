using System.Buffers;
using Mokaterm.Modules.Mqtt.Messages;

namespace Mokaterm.Modules.Mqtt;

/// <summary>
/// What one session keeps of everything the broker sent: a topic tree and, per topic, the newest messages. Every
/// cap is applied here, where the message arrives, so a broker pushing megabytes a second costs a bounded amount
/// of memory no matter what the view is doing.
/// </summary>
/// <remarks>
/// One lock covers the whole store. The MQTTnet receive loop writes under it and the view reads snapshots under
/// it, which is why nothing hands out a live node. The view redraws from <see cref="Version"/> on a timer rather
/// than from an event per message: a thousand messages a second would otherwise be a thousand renders.
/// </remarks>
public sealed class MqttMessageStore
{
	/// <summary>Characters of a payload a topic row shows.</summary>
	public const int PreviewCharacters = 64;

	private readonly Lock _gate = new();
	private readonly MqttStoreCaps _caps;
	private readonly TimeProvider _timeProvider;
	private readonly MqttTopicNode _root = MqttTopicNode.CreateRoot();
	private readonly Dictionary<string, MqttTopicNode> _byTopic = new(StringComparer.Ordinal);

	/// <summary>Every kept message in arrival order, oldest first, whatever topic it belongs to.</summary>
	private readonly LinkedList<MqttKeptMessage> _order = new();

	private long _sequence;
	private long _received;
	private long _dropped;
	private long _keptBytes;
	private long _version;
	private int _topicCount;

	internal MqttMessageStore(MqttStoreCaps caps, TimeProvider timeProvider)
	{
		_caps = caps;
		_timeProvider = timeProvider;
	}

	/// <summary>Counts up on every change. A view compares it to decide whether a redraw has anything to show.</summary>
	public long Version => Interlocked.Read(ref _version);

	/// <summary>Everything the session received, including what the caps refused to keep.</summary>
	public long Received => Interlocked.Read(ref _received);

	/// <summary>Messages the caps refused: a new topic past the topic cap, or one evicted right away.</summary>
	public long Dropped => Interlocked.Read(ref _dropped);

	/// <summary>Messages held right now.</summary>
	public int Kept
	{
		get
		{
			lock (_gate)
			{
				return _order.Count;
			}
		}
	}

	/// <summary>Payload bytes held right now.</summary>
	public long KeptBytes
	{
		get
		{
			lock (_gate)
			{
				return _keptBytes;
			}
		}
	}

	/// <summary>Topics the tree knows.</summary>
	public int TopicCount
	{
		get
		{
			lock (_gate)
			{
				return _topicCount;
			}
		}
	}

	/// <summary>True once a message was refused because the tree is full, which the view says out loud.</summary>
	public bool TopicsAreFull
	{
		get
		{
			lock (_gate)
			{
				return _topicCount >= _caps.MaxTopics;
			}
		}
	}

	/// <summary>
	/// Takes a message from the broker. The payload is copied out of <paramref name="payload"/> here, cut to the
	/// cap, because the sequence belongs to MQTTnet's receive buffer and is reused as soon as its handler returns.
	/// </summary>
	/// <returns>The message as it was kept, or null when a cap refused it.</returns>
	internal MqttMessage? Add(string topic, in ReadOnlySequence<byte> payload, MqttQos qos, bool retain, string? contentType)
	{
		Interlocked.Increment(ref _received);
		if (string.IsNullOrEmpty(topic))
		{
			// A PUBLISH with an empty topic name is a protocol error; MQTTnet may still hand one over.
			Interlocked.Increment(ref _dropped);
			return null;
		}

		ReadOnlyMemory<byte> kept = MqttPayloadFormatter.Copy(payload, _caps.MaxPayloadBytes, out int fullLength);
		MqttMessage message = new()
		{
			Topic = topic,
			ReceivedAt = _timeProvider.GetUtcNow(),
			QualityOfService = qos,
			Retain = retain,
			Payload = kept,
			PayloadLength = fullLength,
			ContentType = contentType,
			Sequence = Interlocked.Increment(ref _sequence),
		};

		lock (_gate)
		{
			MqttTopicNode? node = Resolve(topic);
			if (node is null)
			{
				Interlocked.Increment(ref _dropped);
				return null;
			}

			node.CountReceived(message);
			MqttKeptMessage entry = new(message);
			node.Keep(entry);
			entry.Order = _order.AddLast(entry);
			_keptBytes += message.Payload.Length;

			// The per-topic cap first, so a busy topic evicts its own rather than another topic's.
			while (node.KeptCount > _caps.MessagesPerTopic)
			{
				Drop(node.Evict());
			}

			while ((_order.Count > _caps.TotalMessages || _keptBytes > _caps.TotalPayloadBytes)
				&& _order.First is { Value: { } oldest })
			{
				// A node's own age order is the store's order restricted to that node, so the oldest message of the
				// store is also the oldest of its own topic.
				if (_byTopic.TryGetValue(oldest.Message.Topic, out MqttTopicNode? owner))
				{
					owner.Evict();
				}

				Drop(oldest);
			}

			Interlocked.Increment(ref _version);

			// A payload larger than the whole byte cap is evicted by the loop above the moment it arrives.
			return entry.Order is null ? null : message;
		}
	}

	/// <summary>The topic tree flattened for the view: only the levels under an expanded parent, in tree order.</summary>
	/// <param name="isExpanded">Whether a topic's children are shown. The root's children always are.</param>
	/// <param name="search">Case-insensitive text a topic must contain, itself or in a child. Empty shows everything.</param>
	/// <param name="byTime">Order children by when they last spoke, newest first, instead of by name.</param>
	/// <param name="limit">Most rows to return. A tree wider than this is cut, because the page draws every row.</param>
	public List<MqttTopicRow> Rows(Func<string, bool> isExpanded, string? search, bool byTime, int limit)
	{
		ArgumentNullException.ThrowIfNull(isExpanded);
		List<MqttTopicRow> rows = new(Math.Min(limit, 256));
		string? needle = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
		lock (_gate)
		{
			Walk(_root, depth: 0, isExpanded, needle, byTime, limit, rows);
		}

		return rows;
	}

	/// <summary>The kept messages of one topic, newest first. Empty for a level that never carried a message.</summary>
	public List<MqttMessage> Messages(string? topic, int limit)
	{
		if (string.IsNullOrEmpty(topic))
		{
			return [];
		}

		lock (_gate)
		{
			return _byTopic.TryGetValue(topic, out MqttTopicNode? node) ? node.Snapshot(limit) : [];
		}
	}

	/// <summary>Everything a topic row shows, for the one topic the view has selected.</summary>
	public MqttTopicRow? Row(string? topic)
	{
		if (string.IsNullOrEmpty(topic))
		{
			return null;
		}

		lock (_gate)
		{
			return _byTopic.TryGetValue(topic, out MqttTopicNode? node) ? Describe(node, depth: 0, expanded: false) : null;
		}
	}

	/// <summary>Forgets every topic and message. The counters start again too.</summary>
	public void Clear()
	{
		lock (_gate)
		{
			_root.Children.Clear();
			_byTopic.Clear();
			_order.Clear();
			_topicCount = 0;
			_keptBytes = 0;
			Interlocked.Exchange(ref _received, 0);
			Interlocked.Exchange(ref _dropped, 0);
			Interlocked.Increment(ref _version);
		}
	}

	private static MqttTopicRow Describe(MqttTopicNode node, int depth, bool expanded) => new()
	{
		Path = node.Path,
		Segment = node.Segment,
		Depth = depth,
		HasChildren = node.Children.Count > 0,
		IsExpanded = expanded,
		Kept = node.KeptCount,
		Received = node.Received,
		ReceivedWithChildren = node.ReceivedWithChildren,
		HasRetained = node.HasRetained,
		LastReceivedAt = node.LastReceivedAt,
		Preview = node.Newest is { } newest
			? MqttPayloadFormatter.Preview(newest.Payload.Span, newest.PayloadLength, PreviewCharacters)
			: null,
	};

	/// <summary>
	/// The node for a topic, creating the levels it needs. Null when a cap refuses it: the tree is full, or the
	/// topic is longer or deeper than <see cref="MqttTopicFilters"/> allows. Depth is a hard limit and not a taste:
	/// the tree walk is recursive, and a stack overflow cannot be caught.
	/// </summary>
	private MqttTopicNode? Resolve(string topic)
	{
		if (_byTopic.TryGetValue(topic, out MqttTopicNode? existing))
		{
			return existing;
		}

		if (_topicCount >= _caps.MaxTopics || !MqttTopicFilters.IsWithinLimits(topic))
		{
			return null;
		}

		MqttTopicNode node = _root;
		foreach (string segment in MqttTopicFilters.Split(topic))
		{
			node = node.Child(segment);
		}

		_byTopic[topic] = node;
		_topicCount++;
		return node;
	}

	private void Drop(MqttKeptMessage? evicted)
	{
		if (evicted is null)
		{
			return;
		}

		if (evicted.Order is { List: not null } order)
		{
			_order.Remove(order);
		}

		evicted.Order = null;
		_keptBytes -= evicted.Message.Payload.Length;
		Interlocked.Increment(ref _dropped);
	}

	private static bool Walk(
		MqttTopicNode node,
		int depth,
		Func<string, bool> isExpanded,
		string? needle,
		bool byTime,
		int limit,
		List<MqttTopicRow> rows)
	{
		foreach (MqttTopicNode child in Order(node, byTime))
		{
			if (rows.Count >= limit)
			{
				return false;
			}

			// While a search is on, a level shows when it matches or when something under it does, and the levels
			// in between open themselves so the match is reachable.
			bool matches = needle is null || child.Path.Contains(needle, StringComparison.OrdinalIgnoreCase);
			if (!matches && !HasMatch(child, needle))
			{
				continue;
			}

			bool expanded = child.Children.Count > 0 && (needle is not null || isExpanded(child.Path));
			rows.Add(Describe(child, depth, expanded));
			if (expanded && !Walk(child, depth + 1, isExpanded, needle, byTime, limit, rows))
			{
				return false;
			}
		}

		return true;
	}

	private static IEnumerable<MqttTopicNode> Order(MqttTopicNode node, bool byTime) => byTime
		? node.Children.Values.OrderByDescending(child => child.LastReceivedAt ?? DateTimeOffset.MinValue)
		: node.Children.Values;

	private static bool HasMatch(MqttTopicNode node, string? needle)
	{
		if (needle is null)
		{
			return true;
		}

		foreach (MqttTopicNode child in node.Children.Values)
		{
			if (child.Path.Contains(needle, StringComparison.OrdinalIgnoreCase) || HasMatch(child, needle))
			{
				return true;
			}
		}

		return false;
	}
}
