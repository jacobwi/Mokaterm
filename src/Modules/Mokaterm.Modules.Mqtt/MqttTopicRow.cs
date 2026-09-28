namespace Mokaterm.Modules.Mqtt;

/// <summary>
/// One line of the topic tree as the view draws it. The store builds these under its own lock, so a view never
/// walks live nodes while messages are arriving.
/// </summary>
public sealed record MqttTopicRow
{
	/// <summary>The whole topic up to this level, which is also the row's key.</summary>
	public required string Path { get; init; }

	/// <summary>Just this level of the topic, which is what the row shows.</summary>
	public required string Segment { get; init; }

	public required int Depth { get; init; }

	public required bool HasChildren { get; init; }

	public required bool IsExpanded { get; init; }

	/// <summary>Messages kept for this exact topic.</summary>
	public required int Kept { get; init; }

	/// <summary>Messages seen for this exact topic, kept or not.</summary>
	public required long Received { get; init; }

	/// <summary>Messages seen for this topic and everything under it, which is what a folder row shows.</summary>
	public required long ReceivedWithChildren { get; init; }

	/// <summary>A retained message arrived on this exact topic.</summary>
	public required bool HasRetained { get; init; }

	public DateTimeOffset? LastReceivedAt { get; init; }

	/// <summary>A short, printable rendering of the newest payload, or null when nothing is kept here.</summary>
	public string? Preview { get; init; }
}
