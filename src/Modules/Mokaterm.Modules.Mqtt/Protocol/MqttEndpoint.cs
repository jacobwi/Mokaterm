using System.Globalization;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Mqtt.Protocol;

/// <summary>Where a connection dials, in the shape the transport needs and in the shape a message shows.</summary>
internal sealed record MqttEndpoint
{
	public required string Host { get; init; }

	public required int Port { get; init; }

	public required MqttTransport Transport { get; init; }

	public required bool UseTls { get; init; }

	/// <summary>The WebSocket path, unused by TCP.</summary>
	public string Path { get; init; } = MqttConnectionOptions.DefaultPath;

	/// <summary>What the toolbar and every failure message call this broker.</summary>
	public string Display => Transport == MqttTransport.WebSocket ? WebSocketUri : HostEndpoint.Format(Host, Port);

	/// <summary>The address MQTTnet's WebSocket channel connects to, port always written out.</summary>
	public string WebSocketUri => string.Create(
		CultureInfo.InvariantCulture,
		$"{(UseTls ? "wss" : "ws")}://{Host}:{Port}{(MqttConnectionOptions.IsValidPath(Path) ? Path : MqttConnectionOptions.DefaultPath)}");

	public static MqttEndpoint Create(string host, int port, MqttConnectionOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return new MqttEndpoint
		{
			Host = host,
			Port = port,
			Transport = options.Transport,
			UseTls = options.UseTls,
			Path = options.Path,
		};
	}
}
