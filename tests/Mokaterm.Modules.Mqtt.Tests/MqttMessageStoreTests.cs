using System.Buffers;
using System.Text;
using Microsoft.Extensions.Time.Testing;

namespace Mokaterm.Modules.Mqtt.Tests;

public sealed class MqttMessageStoreTests
{
	private static readonly DateTimeOffset Start = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

	[Fact]
	public void Add_PutsTheMessageInTheTreeAndInTheTopicsOwnList()
	{
		MqttMessageStore store = Create();

		MqttMessage? message = Add(store, "house/kitchen/lamp", "on", retain: true);

		Assert.NotNull(message);
		Assert.Equal("house/kitchen/lamp", message.Topic);
		Assert.True(message.Retain);
		Assert.Equal(1, store.Received);
		Assert.Equal(1, store.Kept);
		Assert.Equal(0, store.Dropped);
		Assert.Equal(1, store.TopicCount);
		Assert.Equal(2, store.KeptBytes);

		MqttMessage only = Assert.Single(store.Messages("house/kitchen/lamp", 10));
		Assert.Equal("on", Text(only));
	}

	[Fact]
	public void Add_CountsEveryLevelAboveTheTopic()
	{
		MqttMessageStore store = Create();
		Add(store, "a/b/c", "1");
		Add(store, "a/b/d", "2");

		List<MqttTopicRow> rows = store.Rows(_ => true, search: null, byTime: false, limit: 50);

		MqttTopicRow a = rows.Single(row => row.Path == "a");
		Assert.Equal(0, a.Received);
		Assert.Equal(2, a.ReceivedWithChildren);
		Assert.True(a.HasChildren);

		MqttTopicRow c = rows.Single(row => row.Path == "a/b/c");
		Assert.Equal(1, c.Received);
		Assert.Equal(1, c.Kept);
		Assert.False(c.HasChildren);
	}

	[Fact]
	public void Add_MarksATopicThatEverCarriedARetainedMessage()
	{
		MqttMessageStore store = Create();
		Add(store, "a", "1", retain: true);
		Add(store, "a", "2", retain: false);

		MqttTopicRow row = Assert.IsType<MqttTopicRow>(store.Row("a"));

		Assert.True(row.HasRetained);
	}

	[Fact]
	public void Add_PastTheTopicCap_CountsTheMessageAndKeepsTheTreeAsItIs()
	{
		MqttMessageStore store = Create(new MqttSettings { MaxTopicCount = MqttSettings.MinTopics });
		for (int index = 0; index < MqttSettings.MinTopics; index++)
		{
			Add(store, $"topic/{index}", "x");
		}

		MqttMessage? refused = Add(store, "topic/one-too-many", "x");

		Assert.Null(refused);
		Assert.True(store.TopicsAreFull);
		Assert.Equal(MqttSettings.MinTopics, store.TopicCount);
		Assert.Equal(MqttSettings.MinTopics + 1, store.Received);
		Assert.Equal(1, store.Dropped);
		Assert.Empty(store.Messages("topic/one-too-many", 10));
	}

	[Fact]
	public void Add_PastTheTopicCap_StillKeepsMessagesOnTopicsItAlreadyKnows()
	{
		MqttMessageStore store = Create(new MqttSettings { MaxTopicCount = MqttSettings.MinTopics });
		for (int index = 0; index < MqttSettings.MinTopics; index++)
		{
			Add(store, $"topic/{index}", "first");
		}

		Assert.NotNull(Add(store, "topic/0", "second"));
		Assert.Equal(2, store.Messages("topic/0", 10).Count);
	}

	[Fact]
	public void Add_RefusesATopicDeeperThanTheTreeWalkCanTake()
	{
		MqttMessageStore store = Create();
		string deep = string.Join('/', Enumerable.Repeat("a", MqttTopicFilters.MaxLevels + 1));

		Assert.Null(Add(store, deep, "x"));
		Assert.Equal(0, store.TopicCount);
		Assert.Equal(1, store.Dropped);
	}

	[Fact]
	public void Add_RefusesAnEmptyTopic()
	{
		MqttMessageStore store = Create();

		Assert.Null(Add(store, "", "x"));
		Assert.Equal(1, store.Dropped);
		Assert.Equal(0, store.TopicCount);
	}

	[Fact]
	public void Add_PastTheTopicsOwnCap_DropsThatTopicsOldest()
	{
		MqttMessageStore store = Create(new MqttSettings { MessagesPerTopic = 2, TotalMessages = 100 });
		Add(store, "a", "1");
		Add(store, "a", "2");
		Add(store, "a", "3");

		List<MqttMessage> kept = store.Messages("a", 10);

		Assert.Equal(["3", "2"], kept.Select(Text));
		Assert.Equal(3, store.Received);
		Assert.Equal(2, store.Kept);
		Assert.Equal(1, store.Dropped);
		Assert.Equal(2, store.KeptBytes);
	}

	[Fact]
	public void Add_PastTheSessionCap_DropsTheOldestWhateverTopicItIsOn()
	{
		MqttMessageStore store = Create(new MqttSettings { MessagesPerTopic = 10, TotalMessages = MqttSettings.MinTotalMessages });
		for (int index = 0; index < MqttSettings.MinTotalMessages; index++)
		{
			Add(store, $"t/{index % 5}", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}

		Assert.Equal(MqttSettings.MinTotalMessages, store.Kept);

		Add(store, "t/9", "newest");

		Assert.Equal(MqttSettings.MinTotalMessages, store.Kept);
		Assert.Equal(1, store.Dropped);

		// The oldest message was the first one on t/0, so that topic lost one and no other did.
		Assert.Equal(MqttSettings.MinTotalMessages / 5 - 1, store.Messages("t/0", 100).Count);
		Assert.Equal(MqttSettings.MinTotalMessages / 5, store.Messages("t/1", 100).Count);
	}

	[Fact]
	public void Add_PastTheByteCap_DropsUntilItIsUnderIt()
	{
		MqttMessageStore store = Create(new MqttSettings
		{
			MessagesPerTopic = 100,
			TotalMessages = 100,
			LargestPayloadKilobytes = 1,
			TotalPayloadKilobytes = MqttSettings.MinTotalPayloadKilobytes,
		});

		// Sixty-four payloads of one kilobyte each fit exactly; the next one pushes the first out.
		string kilobyte = new('x', 1024);
		for (int index = 0; index < 64; index++)
		{
			Add(store, $"t/{index}", kilobyte);
		}

		Assert.Equal(64, store.Kept);
		Assert.Equal(MqttSettings.MinTotalPayloadKilobytes * 1024L, store.KeptBytes);

		Add(store, "t/64", kilobyte);

		Assert.Equal(64, store.Kept);
		Assert.Equal(1, store.Dropped);
		Assert.Empty(store.Messages("t/0", 10));
	}

	[Fact]
	public void Add_CutsAPayloadLargerThanTheCapAndSaysHowLongItReallyWas()
	{
		MqttMessageStore store = Create(new MqttSettings { LargestPayloadKilobytes = 1 });
		string large = new('y', 4096);

		MqttMessage message = Assert.IsType<MqttMessage>(Add(store, "big", large));

		Assert.True(message.IsTruncated);
		Assert.Equal(1024, message.Payload.Length);
		Assert.Equal(4096, message.PayloadLength);
		Assert.Equal(1024, store.KeptBytes);
	}

	[Fact]
	public void Rows_ShowsOnlyTheLevelsUnderAnExpandedParent()
	{
		MqttMessageStore store = Create();
		Add(store, "house/kitchen/lamp", "on");
		Add(store, "house/hall/lock", "locked");

		List<MqttTopicRow> collapsed = store.Rows(_ => false, search: null, byTime: false, limit: 50);
		Assert.Equal(["house"], collapsed.Select(row => row.Path));
		Assert.False(collapsed[0].IsExpanded);

		List<MqttTopicRow> open = store.Rows(path => path == "house", search: null, byTime: false, limit: 50);
		Assert.Equal(["house", "house/hall", "house/kitchen"], open.Select(row => row.Path));
		Assert.True(open[0].IsExpanded);
	}

	[Fact]
	public void Rows_OrdersByNameUnlessAskedForTime()
	{
		MqttMessageStore store = Create();
		Add(store, "zeta", "1");
		Add(store, "alpha", "2");

		Assert.Equal(["alpha", "zeta"], store.Rows(_ => true, null, byTime: false, 50).Select(row => row.Path));
		Assert.Equal(["alpha", "zeta"], store.Rows(_ => true, null, byTime: true, 50).Select(row => row.Path));
	}

	[Fact]
	public void Rows_ByTime_PutsTheTopicThatSpokeLastFirst()
	{
		FakeTimeProvider time = new(Start);
		MqttMessageStore store = Create(timeProvider: time);
		Add(store, "alpha", "1");
		time.Advance(TimeSpan.FromSeconds(5));
		Add(store, "zeta", "2");

		Assert.Equal(["zeta", "alpha"], store.Rows(_ => true, null, byTime: true, 50).Select(row => row.Path));
	}

	[Fact]
	public void Rows_WithASearch_KeepsTheLevelsThatLeadToAMatchAndOpensThem()
	{
		MqttMessageStore store = Create();
		Add(store, "house/kitchen/lamp", "on");
		Add(store, "house/hall/lock", "locked");
		Add(store, "weather/outside", "cold");

		List<MqttTopicRow> rows = store.Rows(_ => false, "lamp", byTime: false, limit: 50);

		Assert.Equal(["house", "house/kitchen", "house/kitchen/lamp"], rows.Select(row => row.Path));
	}

	[Fact]
	public void Rows_StopsAtTheLimit()
	{
		MqttMessageStore store = Create();
		for (int index = 0; index < 40; index++)
		{
			Add(store, $"t{index}", "x");
		}

		Assert.Equal(10, store.Rows(_ => true, null, byTime: false, limit: 10).Count);
	}

	[Fact]
	public void Rows_PreviewsThePayloadAsOneShortLine()
	{
		MqttMessageStore store = Create();
		Add(store, "a", "line one\nline two");

		MqttTopicRow row = store.Rows(_ => true, null, byTime: false, 10).Single();

		Assert.Equal("line one line two", row.Preview);
	}

	[Fact]
	public void Messages_ReturnsTheNewestFirstAndAtMostTheLimit()
	{
		MqttMessageStore store = Create(new MqttSettings { MessagesPerTopic = 10 });
		for (int index = 1; index <= 5; index++)
		{
			Add(store, "a", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}

		Assert.Equal(["5", "4"], store.Messages("a", 2).Select(Text));
	}

	[Fact]
	public void Messages_IsEmptyForALevelThatNeverCarriedOne()
	{
		MqttMessageStore store = Create();
		Add(store, "a/b", "1");

		Assert.Empty(store.Messages("a", 10));
		Assert.Empty(store.Messages(null, 10));
		Assert.Null(store.Row("nothing"));
	}

	[Fact]
	public void Version_MovesOnEveryChangeAndNotOtherwise()
	{
		MqttMessageStore store = Create();
		long before = store.Version;

		Add(store, "a", "1");
		long afterAdd = store.Version;
		Assert.True(afterAdd > before);

		store.Rows(_ => true, null, false, 10);
		Assert.Equal(afterAdd, store.Version);

		store.Clear();
		Assert.True(store.Version > afterAdd);
	}

	[Fact]
	public void Clear_ForgetsEveryTopicAndCounter()
	{
		MqttMessageStore store = Create();
		Add(store, "a/b", "1");
		Add(store, "a/c", "2");

		store.Clear();

		Assert.Equal(0, store.Received);
		Assert.Equal(0, store.Dropped);
		Assert.Equal(0, store.Kept);
		Assert.Equal(0, store.KeptBytes);
		Assert.Equal(0, store.TopicCount);
		Assert.Empty(store.Rows(_ => true, null, false, 10));
	}

	[Fact]
	public void Sequence_CountsUpSoRowsHaveAStableKey()
	{
		MqttMessageStore store = Create();

		MqttMessage first = Assert.IsType<MqttMessage>(Add(store, "a", "1"));
		MqttMessage second = Assert.IsType<MqttMessage>(Add(store, "b", "2"));

		Assert.Equal(1, first.Sequence);
		Assert.Equal(2, second.Sequence);
	}

	[Fact]
	public void Add_TakesTheTimeFromTheTimeProvider()
	{
		FakeTimeProvider time = new(Start);
		MqttMessageStore store = Create(timeProvider: time);

		MqttMessage message = Assert.IsType<MqttMessage>(Add(store, "a", "1"));

		Assert.Equal(Start, message.ReceivedAt);
	}

	[Fact]
	public void ToString_PrintsNeitherTheTopicNorThePayload()
	{
		MqttMessageStore store = Create();
		MqttMessage message = Assert.IsType<MqttMessage>(Add(store, "secret/topic", "token-value"));

		string text = message.ToString();

		Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
		Assert.DoesNotContain("token", text, StringComparison.Ordinal);
		Assert.Contains("PayloadLength = 11", text, StringComparison.Ordinal);
	}

	private static MqttMessageStore Create(MqttSettings? settings = null, TimeProvider? timeProvider = null) =>
		new((settings ?? new MqttSettings()).Caps(), timeProvider ?? new FakeTimeProvider(Start));

	private static MqttMessage? Add(MqttMessageStore store, string topic, string payload, bool retain = false, MqttQos qos = MqttQos.AtMostOnce) =>
		store.Add(topic, new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(payload)), qos, retain, contentType: null);

	private static string Text(MqttMessage message) => Encoding.UTF8.GetString(message.Payload.Span);
}
