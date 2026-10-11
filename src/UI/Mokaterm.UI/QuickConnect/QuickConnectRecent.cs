using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.QuickConnect;

/// <summary>
/// A target quick connect opened once, kept in the vault so the box can offer it again.
/// </summary>
/// <remarks>
/// A class and not a record: it holds a host and a user name, and a record would print both from <c>ToString</c>.
/// Public because <see cref="QuickConnectRow"/> carries it into <see cref="QuickConnectList"/>, whose parameters are.
/// </remarks>
public sealed class QuickConnectRecent
{
	public required string ProtocolId { get; init; }

	public string? Username { get; init; }

	public required string Address { get; init; }

	public int? Port { get; init; }

	public DateTimeOffset ConnectedAt { get; init; }

	public static QuickConnectRecent From(string protocolId, QuickConnectTarget target, DateTimeOffset connectedAt)
	{
		ArgumentNullException.ThrowIfNull(protocolId);
		ArgumentNullException.ThrowIfNull(target);
		return new QuickConnectRecent
		{
			ProtocolId = protocolId.ToLowerInvariant(),
			Username = string.IsNullOrWhiteSpace(target.Username) ? null : target.Username,
			Address = target.Address,
			Port = target.Port,
			ConnectedAt = connectedAt,
		};
	}

	/// <summary>The target this entry stands for, to open it again without reading text.</summary>
	public QuickConnectTarget ToTarget() => new(ProtocolId, Username, Address, Port);

	/// <summary>
	/// The same protocol and target. Host names and user names are compared without case, so typing a machine in
	/// capitals the next time moves the old entry up instead of adding a second one.
	/// </summary>
	public bool IsSameTarget(QuickConnectRecent other)
	{
		ArgumentNullException.ThrowIfNull(other);
		return string.Equals(ProtocolId, other.ProtocolId, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(Username ?? "", other.Username ?? "", StringComparison.OrdinalIgnoreCase)
			&& string.Equals(Address, other.Address, StringComparison.OrdinalIgnoreCase)
			&& Port == other.Port;
	}

	/// <summary>What the box holds for this entry: <c>user@host:port</c>, or the address alone for a name.</summary>
	public string Text => QuickConnectText.Format(Username, Address, Port);

	/// <summary>Whether the entry still names something <paramref name="protocols"/> can open.</summary>
	public bool IsOpenable(IProtocolRegistry protocols)
	{
		ArgumentNullException.ThrowIfNull(protocols);
		return protocols.Find(ProtocolId) is not null && Address.Length > 0;
	}
}
