namespace Mokaterm.Modules.Mqtt.Tests;

public sealed class MqttTopicFiltersTests
{
	[Theory]
	[InlineData("#")]
	[InlineData("sport/#")]
	[InlineData("sport/+/player1")]
	[InlineData("+")]
	[InlineData("+/+")]
	[InlineData("sport/tennis/#")]
	[InlineData("$SYS/#")]
	[InlineData("a//b")]
	public void ValidateFilter_TakesTheFiltersOfTheSpecification(string filter) =>
		Assert.Null(MqttTopicFilters.ValidateFilter(filter));

	[Theory]
	[InlineData("")]
	[InlineData("sport/#/ranking")]
	[InlineData("sport+")]
	[InlineData("sp+ort/x")]
	[InlineData("sport/tennis#")]
	public void ValidateFilter_RefusesWhatABrokerWould(string filter) =>
		Assert.NotNull(MqttTopicFilters.ValidateFilter(filter));

	[Fact]
	public void ValidateFilter_RefusesAFilterLongerThanTheLimit() =>
		Assert.NotNull(MqttTopicFilters.ValidateFilter(new string('a', MqttTopicFilters.MaxLength + 1)));

	[Fact]
	public void ValidateFilter_RefusesANullCharacter() =>
		Assert.NotNull(MqttTopicFilters.ValidateFilter("a/\0/b"));

	[Theory]
	[InlineData("sport/tennis")]
	[InlineData("a")]
	[InlineData("a//b")]
	[InlineData("$SYS/broker/uptime")]
	public void ValidateTopic_TakesANameAMessageMayGoTo(string topic) =>
		Assert.Null(MqttTopicFilters.ValidateTopic(topic));

	[Theory]
	[InlineData("")]
	[InlineData("sport/#")]
	[InlineData("sport/+/x")]
	public void ValidateTopic_RefusesWildcards(string topic) =>
		Assert.NotNull(MqttTopicFilters.ValidateTopic(topic));

	[Theory]
	[InlineData("sport/tennis/player1", "sport/tennis/player1", true)]
	[InlineData("sport/tennis/player1", "sport/tennis/#", true)]
	[InlineData("sport/tennis/player1", "sport/+/player1", true)]
	[InlineData("sport/tennis/player1", "sport/+", false)]
	[InlineData("sport/tennis", "sport/tennis/#", true)]
	[InlineData("sport", "#", true)]
	[InlineData("$SYS/broker/uptime", "#", false)]
	[InlineData("$SYS/broker/uptime", "$SYS/#", true)]
	[InlineData("a/b", "a/b/c", false)]
	public void Matches_AgreesWithWhatABrokerDelivers(string topic, string filter, bool expected) =>
		Assert.Equal(expected, MqttTopicFilters.Matches(topic, filter));

	[Fact]
	public void Matches_IsFalseForNulls()
	{
		Assert.False(MqttTopicFilters.Matches(null, "#"));
		Assert.False(MqttTopicFilters.Matches("a", null));
	}

	[Fact]
	public void Split_KeepsAnEmptyLevel() =>
		Assert.Equal(["a", "", "b"], MqttTopicFilters.Split("a//b"));

	[Fact]
	public void IsReserved_MarksTheTopicsABrokerKeepsToItself()
	{
		Assert.True(MqttTopicFilters.IsReserved("$SYS/broker"));
		Assert.False(MqttTopicFilters.IsReserved("sys/broker"));
		Assert.False(MqttTopicFilters.IsReserved(""));
	}

	[Fact]
	public void IsWithinLimits_RefusesATopicDeepEnoughToOverflowATreeWalk()
	{
		string deep = string.Join('/', Enumerable.Repeat("a", MqttTopicFilters.MaxLevels + 1));

		Assert.False(MqttTopicFilters.IsWithinLimits(deep));
		Assert.True(MqttTopicFilters.IsWithinLimits(string.Join('/', Enumerable.Repeat("a", MqttTopicFilters.MaxLevels))));
	}

	[Fact]
	public void IsWithinLimits_RefusesATopicLongerThanAReceivedOneMayBe()
	{
		Assert.False(MqttTopicFilters.IsWithinLimits(new string('a', MqttTopicFilters.MaxReceivedLength + 1)));
		Assert.True(MqttTopicFilters.IsWithinLimits(new string('a', MqttTopicFilters.MaxReceivedLength)));
		Assert.False(MqttTopicFilters.IsWithinLimits(""));
	}
}
