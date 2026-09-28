using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Mqtt.Tests;

public sealed class MqttConnectionOptionsTests
{
	[Fact]
	public void Default_MatchesTheDefaultsOfAnEmptyOptionsBag()
	{
		MqttConnectionOptions read = MqttConnectionOptions.From(ProtocolOptions.Empty);

		Assert.Equal(MqttConnectionOptions.Default, read);
		Assert.Equal(MqttTransport.Tcp, read.Transport);
		Assert.False(read.UseTls);
		Assert.True(read.CleanStart);
		Assert.Equal(MqttConnectionOptions.DefaultKeepAliveSeconds, read.KeepAliveSeconds);
		Assert.Equal(MqttProtocolLevel.V500, read.Protocol);
		Assert.Equal(MqttConnectionOptions.DefaultPath, read.Path);
		Assert.Null(read.ClientId);
		Assert.Empty(read.Subscriptions);
	}

	[Fact]
	public void ApplyTo_WithDefaults_WritesOnlyTheTwoEnumsItPins()
	{
		ProtocolOptions written = MqttConnectionOptions.Default.ApplyTo(ProtocolOptions.Empty);

		// The transport and the version are pinned even at their default, so a later change of default cannot
		// reinterpret a saved connection. Everything else stays out of the file.
		Assert.Equal(
			[MqttConnectionOptions.ProtocolKey, MqttConnectionOptions.TransportKey],
			written.Keys.OrderBy(key => key, StringComparer.Ordinal));
		Assert.Equal(nameof(MqttTransport.Tcp), written.GetString(MqttConnectionOptions.TransportKey));
		Assert.Equal(nameof(MqttProtocolLevel.V500), written.GetString(MqttConnectionOptions.ProtocolKey));
	}

	[Fact]
	public void ApplyTo_ThenFrom_RoundTripsEveryValue()
	{
		MqttConnectionOptions options = new()
		{
			Transport = MqttTransport.WebSocket,
			UseTls = true,
			Path = "/broker/ws",
			ClientId = "kitchen-probe",
			CleanStart = false,
			KeepAliveSeconds = 15,
			Protocol = MqttProtocolLevel.V311,
			Subscriptions =
			[
				MqttSubscription.Create() with { TopicFilter = "sensors/+/temp", QualityOfService = MqttQos.AtLeastOnce },
				MqttSubscription.Create() with { TopicFilter = "$SYS/#", SubscribeWithSession = false },
			],
		};

		MqttConnectionOptions read = MqttConnectionOptions.From(options.ApplyTo(ProtocolOptions.Empty));

		Assert.Equal(options, read);
	}

	[Fact]
	public void ApplyTo_KeepsKeysThatBelongToAnythingElse()
	{
		ProtocolOptions other = ProtocolOptions.Empty.With("ssh.terminalType", "xterm");

		ProtocolOptions written = (MqttConnectionOptions.Default with { UseTls = true }).ApplyTo(other);

		Assert.Equal("xterm", written.GetString("ssh.terminalType"));
		Assert.Equal("true", written.GetString(MqttConnectionOptions.TlsKey));
	}

	[Fact]
	public void ApplyTo_RewritesEverySubscriptionKey_SoRemovingOneLeavesNoGap()
	{
		MqttSubscription first = MqttSubscription.Create() with { TopicFilter = "a/#" };
		MqttSubscription second = MqttSubscription.Create() with { TopicFilter = "b/#" };
		MqttSubscription third = MqttSubscription.Create() with { TopicFilter = "c/#" };
		ProtocolOptions three = (MqttConnectionOptions.Default with { Subscriptions = [first, second, third] }).ApplyTo(ProtocolOptions.Empty);

		ProtocolOptions two = (MqttConnectionOptions.Default with { Subscriptions = [first, third] }).ApplyTo(three);

		Assert.Equal(2, two.Keys.Count(key => key.StartsWith(MqttConnectionOptions.SubscriptionKeyPrefix, StringComparison.Ordinal)));
		Assert.Equal([first, third], MqttConnectionOptions.From(two).Subscriptions);
	}

	[Fact]
	public void ApplyTo_DropsASubscriptionTheBrokerWouldRefuse()
	{
		MqttSubscription valid = MqttSubscription.Create() with { TopicFilter = "ok/#" };
		MqttSubscription invalid = MqttSubscription.Create() with { TopicFilter = "bad/#/more" };

		ProtocolOptions written = (MqttConnectionOptions.Default with { Subscriptions = [invalid, valid] }).ApplyTo(ProtocolOptions.Empty);

		Assert.Equal([valid], MqttConnectionOptions.From(written).Subscriptions);
	}

	[Fact]
	public void From_CapsTheSubscriptionsItReads()
	{
		ProtocolOptions options = ProtocolOptions.Empty;
		for (int index = 0; index < MqttConnectionOptions.MaxSubscriptions + 10; index++)
		{
			options = options.With(
				$"{MqttConnectionOptions.SubscriptionKeyPrefix}{index:D3}",
				(MqttSubscription.Create() with { TopicFilter = $"topic/{index}" }).Format());
		}

		Assert.Equal(MqttConnectionOptions.MaxSubscriptions, MqttConnectionOptions.From(options).Subscriptions.Count);
	}

	[Fact]
	public void From_DropsAValueItCannotRead()
	{
		ProtocolOptions options = ProtocolOptions.Empty
			.With($"{MqttConnectionOptions.SubscriptionKeyPrefix}00", "not a subscription")
			.With($"{MqttConnectionOptions.SubscriptionKeyPrefix}01", (MqttSubscription.Create() with { TopicFilter = "good/#" }).Format());

		MqttSubscription only = Assert.Single(MqttConnectionOptions.From(options).Subscriptions);

		Assert.Equal("good/#", only.TopicFilter);
	}

	[Fact]
	public void From_DropsADuplicateSubscriptionId()
	{
		MqttSubscription subscription = MqttSubscription.Create() with { TopicFilter = "once/#" };
		ProtocolOptions options = ProtocolOptions.Empty
			.With($"{MqttConnectionOptions.SubscriptionKeyPrefix}00", subscription.Format())
			.With($"{MqttConnectionOptions.SubscriptionKeyPrefix}01", subscription.Format());

		Assert.Single(MqttConnectionOptions.From(options).Subscriptions);
	}

	[Theory]
	[InlineData(MqttTransport.Tcp, false, MqttConnectionOptions.PlainPort)]
	[InlineData(MqttTransport.Tcp, true, MqttConnectionOptions.TlsPort)]
	[InlineData(MqttTransport.WebSocket, false, MqttConnectionOptions.WebSocketPort)]
	[InlineData(MqttTransport.WebSocket, true, MqttConnectionOptions.WebSocketTlsPort)]
	public void DefaultPort_FollowsTheTransportAndTls(MqttTransport transport, bool tls, int expected)
	{
		MqttConnectionOptions options = MqttConnectionOptions.Default with { Transport = transport, UseTls = tls };

		Assert.Equal(expected, options.DefaultPort);
	}

	[Fact]
	public void From_ClampsAKeepAliveOutsideTheWireField()
	{
		ProtocolOptions options = ProtocolOptions.Empty.With(MqttConnectionOptions.KeepAliveKey, "999999");

		Assert.Equal(MqttConnectionOptions.MaxKeepAliveSeconds, MqttConnectionOptions.From(options).KeepAliveSeconds);
	}

	[Fact]
	public void From_FallsBackWhenAnEnumValueIsNotOne()
	{
		ProtocolOptions options = ProtocolOptions.Empty
			.With(MqttConnectionOptions.TransportKey, "77")
			.With(MqttConnectionOptions.ProtocolKey, "V600");

		MqttConnectionOptions read = MqttConnectionOptions.From(options);

		Assert.Equal(MqttTransport.Tcp, read.Transport);
		Assert.Equal(MqttProtocolLevel.V500, read.Protocol);
	}

	[Theory]
	[InlineData("client-1", true)]
	[InlineData("a", true)]
	[InlineData("", false)]
	[InlineData("   ", false)]
	[InlineData("two words", false)]
	[InlineData("tab\there", false)]
	public void IsValidClientId_TakesOneWordOfPrintableText(string clientId, bool expected) =>
		Assert.Equal(expected, MqttConnectionOptions.IsValidClientId(clientId));

	[Fact]
	public void IsValidClientId_RefusesOneLongerThanTheLimit() =>
		Assert.False(MqttConnectionOptions.IsValidClientId(new string('a', MqttConnectionOptions.MaxClientIdLength + 1)));

	[Theory]
	[InlineData("/mqtt", true)]
	[InlineData("/broker/ws", true)]
	[InlineData("mqtt", false)]
	[InlineData("/mqtt?x=1", false)]
	[InlineData("/mqtt#frag", false)]
	[InlineData("/with space", false)]
	[InlineData("", false)]
	public void IsValidPath_TakesOneRootedPathWithNoQuery(string path, bool expected) =>
		Assert.Equal(expected, MqttConnectionOptions.IsValidPath(path));

	[Fact]
	public void From_FallsBackToTheDefaultPathWhenTheStoredOneIsNotUsable()
	{
		ProtocolOptions options = ProtocolOptions.Empty.With(MqttConnectionOptions.PathKey, "no-slash");

		Assert.Equal(MqttConnectionOptions.DefaultPath, MqttConnectionOptions.From(options).Path);
	}

	[Fact]
	public void Equality_ComparesTheSubscriptionsInOrder()
	{
		MqttSubscription first = MqttSubscription.Create() with { TopicFilter = "a/#" };
		MqttSubscription second = MqttSubscription.Create() with { TopicFilter = "b/#" };

		MqttConnectionOptions one = MqttConnectionOptions.Default with { Subscriptions = [first, second] };
		MqttConnectionOptions same = MqttConnectionOptions.Default with { Subscriptions = [first, second] };
		MqttConnectionOptions reversed = MqttConnectionOptions.Default with { Subscriptions = [second, first] };

		Assert.Equal(one, same);
		Assert.Equal(one.GetHashCode(), same.GetHashCode());
		Assert.NotEqual(one, reversed);
	}
}
