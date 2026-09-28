using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Modules.Mqtt.Messages;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Modules.Mqtt.Components;

/// <summary>
/// One message's payload as text, JSON or hex. The rendering is done once per message and format and then held,
/// because a payload of tens of kilobytes would otherwise be formatted again on every redraw.
/// </summary>
public sealed partial class MqttPayloadViewer : ComponentBase
{
	private MqttMessage? _rendered;
	private MqttPayloadFormat _renderedFormat;
	private string _text = "";
	private string? _json;
	private MqttPayloadFormat _format;
	private bool _formatChosen;

	[Parameter]
	public MqttMessage? Message { get; set; }

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private ILogger<MqttPayloadViewer> Logger { get; set; } = default!;

	private bool HasJson => _json is not null;

	private string Rendered => _format switch
	{
		MqttPayloadFormat.Json => _json ?? _text,
		MqttPayloadFormat.Hex => Message is null ? "" : MqttPayloadFormatter.Hex(Message.Payload.Span),
		_ => _text,
	};

	private string SizeText => Message is null
		? ""
		: MqttPayloadFormatter.Size(Message.PayloadLength);

	private string TruncatedTitle => Message is null
		? ""
		: string.Create(
			CultureInfo.CurrentCulture,
			$"Only the first {MqttPayloadFormatter.Size(Message.Payload.Length)} of this payload was kept. Raise the largest payload kept in Settings, MQTT.");

	protected override void OnParametersSet()
	{
		if (Message is null)
		{
			_rendered = null;
			_text = "";
			_json = null;
			return;
		}

		if (ReferenceEquals(_rendered, Message) && _renderedFormat == _format)
		{
			return;
		}

		if (!ReferenceEquals(_rendered, Message))
		{
			_rendered = Message;
			_text = MqttPayloadFormatter.Text(Message.Payload.Span);
			_json = MqttPayloadFormatter.Json(Message.Payload.Span);

			// The first message the viewer shows decides the format; after that the user's choice stands, except
			// that JSON falls back when a message is not JSON.
			if (!_formatChosen)
			{
				_format = _json is not null
					? MqttPayloadFormat.Json
					: MqttPayloadFormatter.LooksLikeText(Message.Payload.Span) ? MqttPayloadFormat.Text : MqttPayloadFormat.Hex;
			}
			else if (_format == MqttPayloadFormat.Json && _json is null)
			{
				_format = MqttPayloadFormat.Text;
			}
		}

		_renderedFormat = _format;
	}

	private void OnFormatChanged(string value)
	{
		if (Enum.TryParse(value, out MqttPayloadFormat format) && Enum.IsDefined(format))
		{
			_format = format;
			_formatChosen = true;
		}
	}

	private async Task CopyAsync()
	{
		string text = Rendered;
		if (text.Length == 0)
		{
			return;
		}

		try
		{
			// A payload can hold a token, so this is a plain clipboard write and never a logged one.
			await Clipboard.WriteTextAsync(text);
			Interaction.Notify(NoticeSeverity.Success, "The payload is on the clipboard.", "MQTT");
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The view is closing.
		}
		catch (Exception ex)
		{
			Logger.LogDebug("Copying a payload failed: {Error}", ex.GetType().Name);
			Interaction.Notify(NoticeSeverity.Warning, "The clipboard cannot be written here.", "MQTT");
		}
	}
}
