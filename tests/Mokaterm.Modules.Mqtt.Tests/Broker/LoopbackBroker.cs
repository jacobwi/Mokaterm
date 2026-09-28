using System.Buffers;
using System.Net;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Mokaterm.Modules.Mqtt.Tests.Broker;

/// <summary>
/// A real MQTT broker on a loopback port, which is what the connect and session tests run against. MQTTnet 5 ships
/// its server in a package of its own, so nothing that ships references it; only the tests and the workbench do.
/// </summary>
internal sealed class LoopbackBroker : IAsyncDisposable
{
	private readonly MqttServerFactory _factory = new();
	private readonly MqttServer _server;
	private readonly string? _username;
	private readonly string? _password;
	private int _disposed;

	private LoopbackBroker(int port, string? username, string? password)
	{
		Port = port;
		_username = username;
		_password = password;
		_server = _factory.CreateMqttServer(_factory.CreateServerOptionsBuilder()
			.WithDefaultEndpoint()
			.WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
			.WithDefaultEndpointPort(port)
			.WithoutEncryptedEndpoint()
			.Build());

		_server.ValidatingConnectionAsync += ValidateAsync;
	}

	public int Port { get; }

	/// <summary>Every CONNECT the broker saw, in order, as client id and user name.</summary>
	public List<(string ClientId, string? UserName)> Connects { get; } = [];

	/// <summary>Filters the broker granted, in order.</summary>
	public List<string> Subscribed { get; } = [];

	/// <summary>Everything clients published, as topic and payload text.</summary>
	public List<(string Topic, string Payload, bool Retain)> Published { get; } = [];

	/// <param name="username">Null takes any login, which is how a broker with open access behaves.</param>
	public static async Task<LoopbackBroker> StartAsync(string? username = null, string? password = null)
	{
		LoopbackBroker broker = new(LoopbackPort.Free(), username, password);
		broker._server.ClientSubscribedTopicAsync += broker.OnSubscribedAsync;
		broker._server.InterceptingPublishAsync += broker.OnPublishAsync;
		await broker._server.StartAsync();
		return broker;
	}

	/// <summary>Publishes as the broker itself, which is how a test makes a message arrive at the client.</summary>
	public Task PublishAsync(string topic, string payload, bool retain = false, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtMostOnce) =>
		PublishAsync(topic, System.Text.Encoding.UTF8.GetBytes(payload), retain, qos);

	public Task PublishAsync(string topic, byte[] payload, bool retain = false, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtMostOnce) =>
		_server.InjectApplicationMessage(
			new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
				.WithTopic(topic)
				.WithPayload(payload)
				.WithRetainFlag(retain)
				.WithQualityOfServiceLevel(qos)
				.Build())
			{
				SenderClientId = "test-broker",
			},
			CancellationToken.None);

	/// <summary>Closes every client connection from the broker's side, which is what a restart looks like.</summary>
	public async Task DisconnectClientsAsync()
	{
		foreach (MqttClientStatus client in await _server.GetClientsAsync())
		{
			await _server.DisconnectClientAsync(client.Id, _factory.CreateMqttServerClientDisconnectOptionsBuilder().Build());
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		try
		{
			await _server.StopAsync(_factory.CreateMqttServerStopOptionsBuilder().Build());
		}
		catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
		{
			// Already down.
		}

		// MqttServer is IDisposable, not IAsyncDisposable, unlike most of MQTTnet 5.
		_server.Dispose();
	}

	private Task ValidateAsync(ValidatingConnectionEventArgs args)
	{
		lock (Connects)
		{
			Connects.Add((args.ClientId, args.UserName));
		}

		if (_username is not null
			&& (!string.Equals(args.UserName, _username, StringComparison.Ordinal) || !string.Equals(args.Password, _password, StringComparison.Ordinal)))
		{
			args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
		}

		return Task.CompletedTask;
	}

	private Task OnSubscribedAsync(ClientSubscribedTopicEventArgs args)
	{
		lock (Subscribed)
		{
			Subscribed.Add(args.TopicFilter.Topic);
		}

		return Task.CompletedTask;
	}

	private Task OnPublishAsync(InterceptingPublishEventArgs args)
	{
		if (args.ClientId != "test-broker")
		{
			lock (Published)
			{
				Published.Add((
					args.ApplicationMessage.Topic,
					System.Text.Encoding.UTF8.GetString(args.ApplicationMessage.Payload.ToArray()),
					args.ApplicationMessage.Retain));
			}
		}

		return Task.CompletedTask;
	}
}
