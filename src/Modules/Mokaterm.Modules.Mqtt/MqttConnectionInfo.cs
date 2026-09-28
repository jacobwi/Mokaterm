namespace Mokaterm.Modules.Mqtt;

/// <summary>What the broker and the handshake said about this connection, for the explorer's toolbar.</summary>
public sealed record MqttConnectionInfo
{
	/// <summary>The client id the broker knows this connection by, its own choice included.</summary>
	public required string ClientId { get; init; }

	/// <summary><c>host:port</c>, or the WebSocket address.</summary>
	public required string Endpoint { get; init; }

	public required MqttTransport Transport { get; init; }

	public required bool IsEncrypted { get; init; }

	/// <summary>The version the broker agreed to, which is the one asked for or nothing.</summary>
	public required MqttProtocolLevel Protocol { get; init; }

	/// <summary>The broker still had a session for this client id, so queued messages may arrive at once.</summary>
	public bool SessionPresent { get; init; }

	/// <summary>The broker's keep-alive, when it asked for one other than ours. Zero means ours stands.</summary>
	public int ServerKeepAliveSeconds { get; init; }

	/// <summary>False when the broker said it does not keep retained messages, so the tree will show none.</summary>
	public bool RetainAvailable { get; init; } = true;

	/// <summary>The highest QoS the broker accepts. A publish above it is refused.</summary>
	public MqttQos MaximumQos { get; init; } = MqttQos.ExactlyOnce;
}
