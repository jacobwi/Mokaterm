using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Sessions.Commands;

/// <summary>
/// A login of a run across hosts as the catalog had it when the host was queued. <see cref="Problem"/> says why it
/// cannot run at all (the login is gone, or its protocol cannot do the run's kind of work), in which case it never
/// connects.
/// </summary>
internal sealed record HostRunTarget(
	Guid ConnectionId,
	HostProfile? Host,
	ConnectionProfile? Connection,
	ProtocolDescriptor? Protocol,
	string? Problem)
{
	/// <param name="whyNot">The run's own rule for a protocol that cannot do its work, such as <see cref="CommandRunEligibility.WhyNot"/>.</param>
	public static HostRunTarget Resolve(ConnectionCatalog catalog, IProtocolRegistry registry, Guid connectionId, Func<ProtocolDescriptor?, string?> whyNot)
	{
		if (catalog.FindConnection(connectionId) is not { } connection)
		{
			return new HostRunTarget(connectionId, null, null, null, "This login no longer exists.");
		}

		if (catalog.FindHost(connection.HostId) is not { } host)
		{
			return new HostRunTarget(connectionId, null, connection, null, "The machine of this login no longer exists.");
		}

		ProtocolDescriptor? protocol = registry.Find(connection.ProtocolId)?.Descriptor;
		return new HostRunTarget(connectionId, host, connection, protocol, whyNot(protocol));
	}

	/// <summary>
	/// <paramref name="row"/> with this login's names from the catalog, so a login renamed between an attempt and the
	/// next shows its new name. A login that is gone keeps the names it had.
	/// </summary>
	public T Describe<T>(T row)
		where T : HostRunResult
	{
		ArgumentNullException.ThrowIfNull(row);
		if (Host is not { } host || Connection is not { } connection)
		{
			return row;
		}

		HostRunResult described = ((HostRunResult)row) with
		{
			Title = connection.GetTitle(host),
			HostName = host.DisplayName,
			Address = host.Address,
			Username = string.IsNullOrWhiteSpace(connection.Username) ? null : connection.Username,
			ProtocolId = connection.ProtocolId,
		};

		return (T)described;
	}
}
