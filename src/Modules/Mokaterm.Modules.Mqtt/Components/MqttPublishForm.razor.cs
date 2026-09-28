using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Moka.Red.Feedback.Toast;

namespace Mokaterm.Modules.Mqtt.Components;

/// <summary>
/// Topic, payload, QoS and retain for one message. The payload is typed as text and sent as UTF-8, which covers
/// what a broker's own tools send; binary payloads are not offered here.
/// </summary>
public sealed partial class MqttPublishForm : ComponentBase
{
	/// <summary>Characters the payload box accepts. A publish is typed, not loaded.</summary>
	public const int MaxPayloadCharacters = 64 * 1024;

	private string _topic = "";
	private string _payload = "";
	private string? _topicError;
	private MqttQos _qos;
	private bool _retain;
	private bool _busy;
	private bool _typed;
	private bool _started;
	private MqttPublishOutcome? _result;
	private string? _appliedTopic;

	[Parameter, EditorRequired]
	public IMqttConnection Connection { get; set; } = default!;

	/// <summary>The topic the form opens with, usually the one selected in the tree.</summary>
	[Parameter]
	public string? Topic { get; set; }

	/// <summary>What a new form starts its QoS at, from the settings.</summary>
	[Parameter]
	public MqttQos DefaultQos { get; set; }

	private bool Busy => _busy;

	private bool CanPublish => _topicError is null && _topic.Length > 0;

	private string PayloadHelp => _payload.Length > 0
		? string.Create(CultureInfo.CurrentCulture, $"{Encoding.UTF8.GetByteCount(_payload)} bytes of UTF-8.")
		: "Empty is allowed, and on a retained topic it is how a value is cleared.";

	private string QosHelp => _qos switch
	{
		MqttQos.AtLeastOnce => "1: the broker acknowledges, so this waits for it.",
		MqttQos.ExactlyOnce => "2: one delivery, four packets.",
		_ => "0: sent and forgotten.",
	};

	private string RetainHelp => _retain
		? "The broker keeps this as the topic's value and gives it to every new subscriber."
		: "Only whoever is subscribed right now receives it.";

	protected override void OnParametersSet()
	{
		if (!_started)
		{
			_started = true;
			_qos = DefaultQos;
		}

		// The topic follows the tree's selection until the user types one of their own.
		if (!_typed && _appliedTopic != Topic)
		{
			_appliedTopic = Topic;
			_topic = Topic ?? "";
			_topicError = _topic.Length == 0 ? null : MqttTopicFilters.ValidateTopic(_topic);
		}
	}

	private static MokaToastSeverity Severity(MqttPublishOutcome result) => result switch
	{
		{ IsSuccess: false } => MokaToastSeverity.Error,
		{ NoMatchingSubscribers: true } => MokaToastSeverity.Warning,
		_ => MokaToastSeverity.Success,
	};

	private static string Describe(MqttPublishOutcome result) => result switch
	{
		{ IsSuccess: false } => result.Error ?? "The message was not sent.",
		{ NoMatchingSubscribers: true } => "Sent, but the broker had nobody subscribed to that topic.",
		_ => "Sent.",
	};

	private void OnTopicChanged(string? value)
	{
		_typed = true;
		_topic = value ?? "";
		_topicError = _topic.Length == 0 ? null : MqttTopicFilters.ValidateTopic(_topic);
		_result = null;
	}

	private void OnPayloadChanged(string? value)
	{
		string payload = value ?? "";
		_payload = payload.Length > MaxPayloadCharacters ? payload[..MaxPayloadCharacters] : payload;
		_result = null;
	}

	private void OnQosChanged(string value)
	{
		if (Enum.TryParse(value, out MqttQos qos) && Enum.IsDefined(qos))
		{
			_qos = qos;
		}
	}

	private void OnRetainChanged(bool value) => _retain = value;

	private async Task PublishAsync()
	{
		if (!CanPublish || _busy)
		{
			return;
		}

		_busy = true;
		_result = null;
		try
		{
			_result = await Connection.PublishAsync(MqttPublishRequest.FromText(_topic, _payload, _qos, _retain));
		}
		finally
		{
			_busy = false;
		}
	}
}
