namespace Mokaterm.Modules.Mqtt.Tests;

public sealed class MqttSubscriptionTests
{
	[Fact]
	public void Create_StartsAtEverythingAndSubscribesWithTheSession()
	{
		MqttSubscription subscription = MqttSubscription.Create();

		Assert.NotEqual(Guid.Empty, subscription.Id);
		Assert.Equal(MqttTopicFilters.Everything, subscription.TopicFilter);
		Assert.Equal(MqttQos.AtMostOnce, subscription.QualityOfService);
		Assert.True(subscription.SubscribeWithSession);
		Assert.Null(subscription.Validate());
	}

	[Fact]
	public void Format_ThenParse_RoundTripsEveryField()
	{
		MqttSubscription subscription = MqttSubscription.Create() with
		{
			TopicFilter = "sensors/+/temperature",
			QualityOfService = MqttQos.ExactlyOnce,
			SubscribeWithSession = false,
		};

		Assert.Equal(subscription, MqttSubscription.Parse(subscription.Format()));
	}

	[Fact]
	public void Parse_KeepsAFilterThatContainsTheSeparator()
	{
		// The filter is written last and unsplit exactly so that a topic level may contain a pipe.
		MqttSubscription subscription = MqttSubscription.Create() with { TopicFilter = "odd|name/#" };

		Assert.Equal("odd|name/#", MqttSubscription.Parse(subscription.Format())?.TopicFilter);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("not-a-guid|AtMostOnce|auto|#")]
	[InlineData("2c0c1b3e-4f5a-4a5b-9c1d-0e1f2a3b4c5d|Sometimes|auto|#")]
	[InlineData("2c0c1b3e-4f5a-4a5b-9c1d-0e1f2a3b4c5d|AtMostOnce|auto")]
	public void Parse_ReturnsNullForAValueItCannotRead(string value) =>
		Assert.Null(MqttSubscription.Parse(value));

	[Fact]
	public void Parse_ReturnsNullForAFilterTheBrokerWouldRefuse()
	{
		string stored = (MqttSubscription.Create() with { TopicFilter = "a/#/b" }).Format();

		Assert.Null(MqttSubscription.Parse(stored));
	}

	[Fact]
	public void Validate_ReportsWhatIsWrongWithTheFilter()
	{
		Assert.NotNull((MqttSubscription.Create() with { TopicFilter = "" }).Validate());
		Assert.NotNull((MqttSubscription.Create() with { TopicFilter = "a/b+" }).Validate());
		Assert.Null((MqttSubscription.Create() with { TopicFilter = "a/+/b" }).Validate());
	}
}
