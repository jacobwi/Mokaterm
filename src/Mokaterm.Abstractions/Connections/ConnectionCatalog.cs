namespace Mokaterm.Abstractions.Connections;

/// <summary>An immutable snapshot of every folder, host and connection.</summary>
public sealed record ConnectionCatalog(
	IReadOnlyList<ConnectionFolder> Folders,
	IReadOnlyList<HostProfile> Hosts,
	IReadOnlyList<ConnectionProfile> Connections)
{
	public static ConnectionCatalog Empty { get; } = new([], [], []);

	public ConnectionFolder? FindFolder(Guid id) => Folders.FirstOrDefault(folder => folder.Id == id);

	public HostProfile? FindHost(Guid id) => Hosts.FirstOrDefault(host => host.Id == id);

	public ConnectionProfile? FindConnection(Guid id) => Connections.FirstOrDefault(connection => connection.Id == id);

	/// <summary>Direct child folders of <paramref name="parentId"/>; null for top level.</summary>
	public IEnumerable<ConnectionFolder> FoldersIn(Guid? parentId) =>
		Folders.Where(folder => folder.ParentId == parentId).OrderBy(folder => folder.Order).ThenBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase);

	/// <summary>Hosts directly inside <paramref name="folderId"/>; null for top level.</summary>
	public IEnumerable<HostProfile> HostsIn(Guid? folderId) =>
		Hosts.Where(host => host.FolderId == folderId).OrderBy(host => host.DisplayName, StringComparer.CurrentCultureIgnoreCase);

	public IEnumerable<ConnectionProfile> ConnectionsOf(Guid hostId) =>
		Connections.Where(connection => connection.HostId == hostId).OrderBy(connection => connection.ProtocolId, StringComparer.Ordinal).ThenBy(connection => connection.Username, StringComparer.CurrentCultureIgnoreCase);
}
