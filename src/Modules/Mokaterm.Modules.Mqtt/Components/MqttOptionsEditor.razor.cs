using System.Globalization;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Mqtt.Protocol;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Mqtt.Components;

/// <summary>The MQTT part of the connection editor: transport, TLS, client id, keep-alive and the saved subscriptions.</summary>
public partial class MqttOptionsEditor : ConnectionOptionsEditorBase<MqttConnectionOptions>
{
	private readonly FieldDraft<string> _clientId = new();
	private readonly FieldDraft<string> _path = new();

	private string TransportHelp => Current.Transport == MqttTransport.WebSocket
		? "MQTT inside WebSocket frames, which is how a broker behind an HTTP reverse proxy is reached."
		: "A plain MQTT socket, which is what a broker's 1883 and 8883 answer.";

	private string TlsHelp => Current.UseTls
		? string.Create(CultureInfo.CurrentCulture, $"The broker's certificate goes through the same trust prompt as an SSH host key. An empty port means {Current.DefaultPort}.")
		: string.Create(CultureInfo.CurrentCulture, $"Nothing is encrypted: the login and every payload travel in clear text. An empty port means {Current.DefaultPort}.");

	private string ProtocolHelp => Current.Protocol switch
	{
		MqttProtocolLevel.V311 => "3.1.1: every broker and device speaks it.",
		MqttProtocolLevel.V310 => "3.1: only for devices old enough to need it.",
		_ => "5.0: reason codes and content types, so a refusal says why. A broker that only speaks 3.1.1 refuses it.",
	};

	private static string ClientIdHelp =>
		"The name the broker lists this connection under. Empty makes a new one per session, which is what keeps two sessions from taking each other's connection down.";

	private string KeepAliveHelp => Current.KeepAliveSeconds == 0
		? "0 sends no pings, so a connection that dies quietly is only noticed when something is published."
		: "Idle seconds before a ping. The broker drops a client it has not heard from in one and a half of these.";

	private string CleanStartHelp => Current.CleanStart
		? "The broker forgets any earlier session, so nothing queued arrives at connect."
		: string.Create(
			CultureInfo.CurrentCulture,
			$"The broker keeps this client id's subscriptions and queued messages for {MqttClientOptionsFactory.KeptSessionSeconds / 60} minutes, so a reconnect picks up what it missed.");

	private static string SubscriptionsHelp =>
		"Sent as soon as the session connects. The explorer can add more for one session without saving them here.";

	protected override MqttConnectionOptions From(ProtocolOptions options) => MqttConnectionOptions.From(options);

	protected override ProtocolOptions ApplyTo(MqttConnectionOptions current, ProtocolOptions options) => current.ApplyTo(options);

	/// <summary>TLS answers on 8883 and a secure WebSocket on 443, so the port field shows that rather than 1883.</summary>
	protected override void OnCurrentChanged() =>
		ReportDefaultPort(Current.DefaultPort == MqttConnectionOptions.PlainPort ? null : Current.DefaultPort);

	private Task OnTransportChangedAsync(string value) =>
		Enum.TryParse(value, out MqttTransport transport) && Enum.IsDefined(transport)
			? ChangeAsync(Current with { Transport = transport })
			: Task.CompletedTask;

	private Task OnProtocolChangedAsync(string value) =>
		Enum.TryParse(value, out MqttProtocolLevel protocol) && Enum.IsDefined(protocol)
			? ChangeAsync(Current with { Protocol = protocol })
			: Task.CompletedTask;

	private Task OnTlsChangedAsync(bool value) => ChangeAsync(Current with { UseTls = value });

	private Task OnCleanStartChangedAsync(bool value) => ChangeAsync(Current with { CleanStart = value });

	private Task OnPathChangedAsync(string? value)
	{
		string path = value ?? "";
		if (path.Length == 0)
		{
			_path.Clear();
			return ChangeAsync(Current with { Path = MqttConnectionOptions.DefaultPath });
		}

		if (!MqttConnectionOptions.IsValidPath(path))
		{
			_path.Reject(path, "A path starts with / and carries no spaces or query string.");
			return Task.CompletedTask;
		}

		_path.Clear();
		return ChangeAsync(Current with { Path = path });
	}

	private Task OnClientIdChangedAsync(string? value)
	{
		string clientId = value ?? "";
		if (clientId.Length == 0)
		{
			_clientId.Clear();
			return ChangeAsync(Current with { ClientId = null });
		}

		if (!MqttConnectionOptions.IsValidClientId(clientId))
		{
			_clientId.Reject(
				clientId,
				string.Create(CultureInfo.CurrentCulture, $"A client id has no spaces and is at most {MqttConnectionOptions.MaxClientIdLength} characters."));
			return Task.CompletedTask;
		}

		_clientId.Clear();
		return ChangeAsync(Current with { ClientId = clientId });
	}

	private Task OnKeepAliveChangedAsync(int value) =>
		ChangeAsync(Current with { KeepAliveSeconds = Math.Clamp(value, 0, MqttConnectionOptions.MaxKeepAliveSeconds) });

	private Task OnSubscriptionsChangedAsync(IReadOnlyList<MqttSubscription> subscriptions) =>
		ChangeAsync(Current with { Subscriptions = subscriptions });
}
