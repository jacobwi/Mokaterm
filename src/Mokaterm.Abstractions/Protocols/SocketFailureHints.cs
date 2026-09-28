namespace Mokaterm.Abstractions.Protocols;

/// <summary>
/// What one protocol adds to the shared sentences in <see cref="SocketFailures"/>. Two branches are worth more than the
/// shared wording, because only the protocol knows which port it listens on and what a server that closes a plain
/// connection is usually telling you.
/// </summary>
public sealed record SocketFailureHints
{
	/// <summary>Nothing to add.</summary>
	public static SocketFailureHints None { get; } = new();

	/// <summary>Follows "{endpoint} refused the connection.", normally naming the ports the protocol listens on.</summary>
	public string? Refused { get; init; }

	/// <summary>Follows "{endpoint} closed the connection unexpectedly."</summary>
	public string? Reset { get; init; }
}
