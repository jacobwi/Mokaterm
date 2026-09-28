using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Mokaterm.DevHost.Demo.Mqtt;

/// <summary>
/// A real MQTT broker on a loopback port, so the explorer can be used without one of your own. It checks the login it
/// generated for this run, then publishes a small house of topics that change on a timer: retained values, a JSON
/// payload, a binary one and a counter that never stops.
/// </summary>
internal sealed class DemoMqttServer : IAsyncDisposable
{
	private const string BaseTopic = "mokaterm/demo";

	private readonly TimeProvider _timeProvider;
	private readonly ILogger<DemoMqttServer> _logger;
	private readonly MqttServerFactory _factory = new();
	private readonly MqttServer _server;
	private readonly CancellationTokenSource _stopping = new();
	private Task _publishing = Task.CompletedTask;
	private int _disposed;

	public DemoMqttServer(TimeProvider timeProvider, ILogger<DemoMqttServer> logger)
	{
		_timeProvider = timeProvider;
		_logger = logger;
		Port = FreePort();
		_server = _factory.CreateMqttServer(_factory.CreateServerOptionsBuilder()
			.WithDefaultEndpoint()
			.WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
			.WithDefaultEndpointPort(Port)
			.WithoutEncryptedEndpoint()
			.Build());

		_server.ValidatingConnectionAsync += ValidateAsync;
	}

	public int Port { get; }

	/// <summary>The name the seeded login sends in its CONNECT.</summary>
	public string Username { get; } = "explorer";

	/// <summary>Random for every run, and handed to the seeded login, which is why nobody ever types it.</summary>
	public string Password { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(9));

	public async Task StartAsync()
	{
		await _server.StartAsync();
		await SeedRetainedAsync();
		_publishing = PublishAsync(_stopping.Token);
		_logger.LogInformation("The demo MQTT broker listens on 127.0.0.1:{Port}", Port);
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await _stopping.CancelAsync();
		try
		{
			await _publishing;
		}
		catch (OperationCanceledException)
		{
			// Stopping.
		}

		try
		{
			await _server.StopAsync(_factory.CreateMqttServerStopOptionsBuilder().Build());
		}
		catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
		{
			// Already down.
		}

		// MqttServer is IDisposable and not IAsyncDisposable, unlike most of MQTTnet 5.
		_server.Dispose();
		_stopping.Dispose();
	}

	private static int FreePort()
	{
		TcpListener probe = new(IPAddress.Loopback, 0);
		probe.Start();
		int port = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();
		return port;
	}

	private Task ValidateAsync(ValidatingConnectionEventArgs args)
	{
		// A broker that takes anyone would not show the login path at all, so this one checks.
		if (!string.Equals(args.UserName, Username, StringComparison.Ordinal) || !string.Equals(args.Password, Password, StringComparison.Ordinal))
		{
			args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
			args.ReasonString = "The demo broker expects the login the workbench seeded.";
		}

		return Task.CompletedTask;
	}

	/// <summary>Values that are already there when a client subscribes, which is what retained messages are for.</summary>
	private async Task SeedRetainedAsync()
	{
		await PublishAsync($"{BaseTopic}/house/kitchen/lamp/state", "on", retain: true);
		await PublishAsync($"{BaseTopic}/house/kitchen/lamp/brightness", "72", retain: true);
		await PublishAsync($"{BaseTopic}/house/hall/lock/state", "locked", retain: true);
		await PublishAsync($"{BaseTopic}/house/garage/door/state", "closed", retain: true);
		await PublishAsync($"{BaseTopic}/status/version", "1.4.2", retain: true);
		await PublishAsync(
			$"{BaseTopic}/status/build",
			"""{"version":"1.4.2","commit":"9f2c1ab","builtAt":"2026-09-01T10:15:00Z","flags":["mqtt","tls"]}""",
			retain: true);
	}

	private async Task PublishAsync(CancellationToken cancellationToken)
	{
		long tick = 0;
		byte[] binary = new byte[48];
		using PeriodicTimer timer = new(TimeSpan.FromSeconds(1), _timeProvider);
		while (await timer.WaitForNextTickAsync(cancellationToken))
		{
			tick++;
			double temperature = 19 + (Math.Sin(tick / 9.0) * 4);
			await PublishAsync($"{BaseTopic}/house/kitchen/sensor/temperature", temperature.ToString("0.0", CultureInfo.InvariantCulture), retain: true);
			await PublishAsync($"{BaseTopic}/house/kitchen/sensor/humidity", (40 + (tick % 17)).ToString(CultureInfo.InvariantCulture), retain: true);
			await PublishAsync($"{BaseTopic}/counter", tick.ToString(CultureInfo.InvariantCulture), retain: false);

			if (tick % 3 == 0)
			{
				await PublishAsync(
					$"{BaseTopic}/telemetry/reading",
					string.Create(
						CultureInfo.InvariantCulture,
						$"{{\"seq\":{tick},\"temperature\":{temperature:0.00},\"battery\":{95 - (tick % 40)},\"ok\":true}}"),
					retain: false);
			}

			if (tick % 5 == 0)
			{
				// A payload that is not text, so the viewer opens on its hex dump.
				RandomNumberGenerator.Fill(binary);
				await _server.InjectApplicationMessage(
					new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
						.WithTopic($"{BaseTopic}/telemetry/frame")
						.WithPayload(binary)
						.Build())
					{
						SenderClientId = "demo-broker",
					},
					cancellationToken);
			}

			if (tick % 7 == 0)
			{
				await PublishAsync($"{BaseTopic}/house/hall/motion", tick % 14 == 0 ? "clear" : "detected", retain: false);
			}
		}
	}

	private Task PublishAsync(string topic, string payload, bool retain) =>
		_server.InjectApplicationMessage(
			new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
				.WithTopic(topic)
				.WithPayload(payload)
				.WithRetainFlag(retain)
				.Build())
			{
				SenderClientId = "demo-broker",
			},
			CancellationToken.None);
}
