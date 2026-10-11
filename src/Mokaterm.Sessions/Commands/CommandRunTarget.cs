using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Sessions.Commands;

/// <summary>
/// A login of a command run as the catalog had it when the host was queued. <see cref="Problem"/> says why it cannot
/// run at all (the login is gone, or its protocol has no way to run a command), in which case it never connects.
/// </summary>
internal sealed record CommandRunTarget(
	Guid ConnectionId,
	HostProfile? Host,
	ConnectionProfile? Connection,
	ProtocolDescriptor? Protocol,
	string? Problem)
{
	public static CommandRunTarget Resolve(ConnectionCatalog catalog, IProtocolRegistry registry, Guid connectionId)
	{
		if (catalog.FindConnection(connectionId) is not { } connection)
		{
			return new CommandRunTarget(connectionId, null, null, null, "This login no longer exists.");
		}

		if (catalog.FindHost(connection.HostId) is not { } host)
		{
			return new CommandRunTarget(connectionId, null, connection, null, "The machine of this login no longer exists.");
		}

		ProtocolDescriptor? protocol = registry.Find(connection.ProtocolId)?.Descriptor;
		return new CommandRunTarget(connectionId, host, connection, protocol, CommandRunEligibility.WhyNot(protocol));
	}

	/// <summary>
	/// The row for this login: its names from the catalog, its state and output from <paramref name="previous"/>, so a
	/// login renamed between a run and its retry shows its new name. A login that is gone keeps the names it had.
	/// </summary>
	public CommandHostResult Describe(CommandHostResult? previous)
	{
		CommandHostResult row = previous ?? new CommandHostResult
		{
			ConnectionId = ConnectionId,
			Title = "Deleted login",
			HostName = "",
			Address = "",
		};

		return Host is { } host && Connection is { } connection
			? row with
			{
				Title = connection.GetTitle(host),
				HostName = host.DisplayName,
				Address = host.Address,
				Username = string.IsNullOrWhiteSpace(connection.Username) ? null : connection.Username,
				ProtocolId = connection.ProtocolId,
			}
			: row;
	}
}
