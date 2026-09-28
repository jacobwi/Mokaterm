using System.Globalization;
using System.Text;

namespace Mokaterm.Modules.Mqtt;

/// <summary>
/// One message the session received, as the explorer keeps it. A payload can hold a token, so the generated
/// <c>ToString</c> is replaced: nothing here prints the topic or the bytes.
/// </summary>
public sealed record MqttMessage
{
	/// <summary>The topic it was published to. Never a filter: wildcards do not travel in a PUBLISH.</summary>
	public required string Topic { get; init; }

	public required DateTimeOffset ReceivedAt { get; init; }

	public required MqttQos QualityOfService { get; init; }

	/// <summary>The broker held this message for new subscribers, which is why it arrived without anyone publishing.</summary>
	public required bool Retain { get; init; }

	/// <summary>The payload as far as it was kept. Empty is normal: that is how a retained message is cleared.</summary>
	public required ReadOnlyMemory<byte> Payload { get; init; }

	/// <summary>How long the payload really was, which is more than <see cref="Payload"/> when it was cut.</summary>
	public required int PayloadLength { get; init; }

	/// <summary>MQTT 5 content type, when the publisher set one.</summary>
	public string? ContentType { get; init; }

	/// <summary>Counts up per session, so rows have a stable key and the age order is total.</summary>
	public required long Sequence { get; init; }

	/// <summary>True when the payload was larger than the store keeps and only its start is here.</summary>
	public bool IsTruncated => PayloadLength > Payload.Length;

	/// <summary>Local time as the message list shows it, to the second.</summary>
	public string TimeText => ReceivedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

	/// <summary>The full local timestamp, for a row's tooltip.</summary>
	public string TimestampText => ReceivedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

	private bool PrintMembers(StringBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		// Neither the topic nor the payload: both can carry an identifier or a token into a log line.
		builder.Append(
			CultureInfo.InvariantCulture,
			$"Sequence = {Sequence}, QualityOfService = {QualityOfService}, Retain = {Retain}, PayloadLength = {PayloadLength}");
		return true;
	}
}
