namespace Mokaterm.Modules.Mqtt.Tests;

public sealed class MqttSettingsTests
{
	[Fact]
	public void SectionKey_IsTheModuleId() => Assert.Equal("mqtt", MqttSettings.SectionKey);

	[Fact]
	public void Defaults_AreBoundedAndAlreadyInsideTheirRanges()
	{
		MqttSettings settings = new();

		Assert.Equal(settings, settings.Clamped());
		Assert.Equal(MqttQos.AtMostOnce, settings.DefaultQualityOfService);
		Assert.False(settings.SortTopicsByTime);
	}

	[Fact]
	public void Clamped_PullsEveryValueIntoItsRange()
	{
		MqttSettings clamped = new MqttSettings
		{
			ConnectTimeoutSeconds = 0,
			AuthenticationAttempts = 0,
			MessagesPerTopic = 0,
			TotalMessages = 0,
			LargestPayloadKilobytes = 0,
			TotalPayloadKilobytes = 0,
			MaxTopicCount = 0,
		}.Clamped();

		Assert.Equal(MqttSettings.MinConnectTimeoutSeconds, clamped.ConnectTimeoutSeconds);
		Assert.Equal(1, clamped.AuthenticationAttempts);
		Assert.Equal(MqttSettings.MinMessagesPerTopic, clamped.MessagesPerTopic);
		Assert.Equal(MqttSettings.MinTotalMessages, clamped.TotalMessages);
		Assert.Equal(MqttSettings.MinPayloadKilobytes, clamped.LargestPayloadKilobytes);
		Assert.Equal(MqttSettings.MinTotalPayloadKilobytes, clamped.TotalPayloadKilobytes);
		Assert.Equal(MqttSettings.MinTopics, clamped.MaxTopicCount);
	}

	[Fact]
	public void Clamped_PullsEveryValueDownFromAboveItsRange()
	{
		MqttSettings clamped = new MqttSettings
		{
			ConnectTimeoutSeconds = int.MaxValue,
			AuthenticationAttempts = int.MaxValue,
			MessagesPerTopic = int.MaxValue,
			TotalMessages = int.MaxValue,
			LargestPayloadKilobytes = int.MaxValue,
			TotalPayloadKilobytes = int.MaxValue,
			MaxTopicCount = int.MaxValue,
		}.Clamped();

		Assert.Equal(MqttSettings.MaxConnectTimeoutSeconds, clamped.ConnectTimeoutSeconds);
		Assert.Equal(MqttSettings.MaxAuthenticationAttempts, clamped.AuthenticationAttempts);
		Assert.Equal(MqttSettings.MaxMessagesPerTopic, clamped.MessagesPerTopic);
		Assert.Equal(MqttSettings.MaxTotalMessages, clamped.TotalMessages);
		Assert.Equal(MqttSettings.MaxPayloadKilobytes, clamped.LargestPayloadKilobytes);
		Assert.Equal(MqttSettings.MaxTotalPayloadKilobytes, clamped.TotalPayloadKilobytes);
		Assert.Equal(MqttSettings.MaxTopics, clamped.MaxTopicCount);
	}

	[Fact]
	public void Clamped_ReplacesAQosThatIsNotOne() =>
		Assert.Equal(MqttQos.AtMostOnce, (new MqttSettings { DefaultQualityOfService = (MqttQos)9 }).Clamped().DefaultQualityOfService);

	[Fact]
	public void Caps_TakeTheClampedValuesAndTurnKilobytesIntoBytes()
	{
		MqttStoreCaps caps = new MqttSettings
		{
			MessagesPerTopic = 7,
			TotalMessages = 700,
			LargestPayloadKilobytes = 2,
			TotalPayloadKilobytes = 128,
			MaxTopicCount = 0,
		}.Caps();

		Assert.Equal(7, caps.MessagesPerTopic);
		Assert.Equal(700, caps.TotalMessages);
		Assert.Equal(2 * 1024, caps.MaxPayloadBytes);
		Assert.Equal(128 * 1024, caps.TotalPayloadBytes);
		Assert.Equal(MqttSettings.MinTopics, caps.MaxTopics);
	}

	[Fact]
	public void ConnectTimeout_IsTheSecondsAsATimeSpan() =>
		Assert.Equal(TimeSpan.FromSeconds(9), (new MqttSettings { ConnectTimeoutSeconds = 9 }).ConnectTimeout);
}
