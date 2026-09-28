using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Modules.Mqtt.Messages;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Mqtt.Components;

/// <summary>The kept messages of the selected topic, newest first.</summary>
public sealed partial class MqttMessageList : ComponentBase
{
	/// <summary>Characters of a payload a row previews.</summary>
	private const int PreviewCharacters = 80;

	[Parameter]
	public string? Topic { get; set; }

	[Parameter, EditorRequired]
	public IReadOnlyList<MqttMessage> Messages { get; set; } = [];

	/// <summary>Everything the topic ever received, which is more than the list shows once messages age out.</summary>
	[Parameter]
	public long Received { get; set; }

	[Parameter]
	public EventCallback<MqttMessage> SelectedChanged { get; set; }

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private ILogger<MqttMessageList> Logger { get; set; } = default!;

	private string CountText => Received > Messages.Count
		? string.Create(CultureInfo.CurrentCulture, $"{Messages.Count} of {Received}")
		: string.Create(CultureInfo.CurrentCulture, $"{Messages.Count}");

	private static string QosText(MqttMessage message) =>
		((int)message.QualityOfService).ToString(CultureInfo.InvariantCulture);

	private static string SizeText(MqttMessage message) => MqttPayloadFormatter.Size(message.PayloadLength);

	private static string Preview(MqttMessage message) =>
		MqttPayloadFormatter.Preview(message.Payload.Span, message.PayloadLength, PreviewCharacters);

	private Task OnRowClick(MqttMessage message) => SelectedChanged.InvokeAsync(message);

	private async Task CopyTopicAsync()
	{
		if (Topic is not { Length: > 0 } topic)
		{
			return;
		}

		try
		{
			await Clipboard.WriteTextAsync(topic);
			Interaction.Notify(NoticeSeverity.Success, "The topic is on the clipboard.", "MQTT");
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The view is closing.
		}
		catch (Exception ex)
		{
			Logger.LogDebug("Copying a topic failed: {Error}", ex.GetType().Name);
			Interaction.Notify(NoticeSeverity.Warning, "The clipboard cannot be written here.", "MQTT");
		}
	}
}
