using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.Commands;

/// <summary>The saved logins a command run starts from: a folder, a host, a saved command's scope or a selection in the tree.</summary>
internal static class CommandRunTargets
{
	/// <summary>The logins of every host in <paramref name="folderId"/> and in every folder below it, in tree order.</summary>
	public static List<ConnectionProfile> InFolder(ConnectionCatalog catalog, Guid folderId)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		List<ConnectionProfile> logins = [];

		// A damaged catalog could make a folder its own ancestor; each folder is walked once.
		HashSet<Guid> visited = [];
		Stack<Guid> pending = new([folderId]);
		while (pending.TryPop(out Guid current))
		{
			if (!visited.Add(current))
			{
				continue;
			}

			foreach (HostProfile host in catalog.HostsIn(current))
			{
				logins.AddRange(catalog.ConnectionsOf(host.Id));
			}

			foreach (ConnectionFolder child in catalog.FoldersIn(current).Reverse())
			{
				pending.Push(child.Id);
			}
		}

		return logins;
	}

	/// <summary>
	/// The logins a saved command's scope names: its own login, or every login on its machine. A global command names
	/// none, so the user picks.
	/// </summary>
	public static List<ConnectionProfile> ForSnippet(ConnectionCatalog catalog, CommandSnippet snippet)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		ArgumentNullException.ThrowIfNull(snippet);
		switch (snippet.Scope)
		{
			case CommandSnippetScope.Connection when snippet.ConnectionId is { } connectionId && catalog.FindConnection(connectionId) is { } login:
				return [login];

			case CommandSnippetScope.Host when !string.IsNullOrWhiteSpace(snippet.Host):
				return
				[
					.. catalog.Hosts
						.Where(host => string.Equals(host.Address, snippet.Host, StringComparison.OrdinalIgnoreCase))
						.SelectMany(host => catalog.ConnectionsOf(host.Id)),
				];

			default:
				return [];
		}
	}

	/// <summary>Null when <paramref name="login"/> can run a command; otherwise why it cannot.</summary>
	public static string? Unavailable(ConnectionProfile login, IProtocolRegistry protocols)
	{
		ArgumentNullException.ThrowIfNull(login);
		ArgumentNullException.ThrowIfNull(protocols);
		return CommandRunEligibility.WhyNot(protocols.Find(login.ProtocolId)?.Descriptor);
	}

	/// <summary>The ids of the logins that can run a command, in order and each once.</summary>
	public static List<Guid> Runnable(IEnumerable<ConnectionProfile> logins, IProtocolRegistry protocols) =>
		[.. logins.Where(login => Unavailable(login, protocols) is null).Select(login => login.Id).Distinct()];
}
