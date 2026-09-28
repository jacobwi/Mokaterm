using Mokaterm.Abstractions.Connections;

namespace Mokaterm.UI.Connections;

internal static class FolderPaths
{
	/// <summary>
	/// Full paths such as <c>Clients / Acme / Web</c> for every folder. Stops at a missing parent or a parent cycle,
	/// so damaged data still produces a readable path.
	/// </summary>
	public static Dictionary<Guid, string> Build(ConnectionCatalog catalog)
	{
		Dictionary<Guid, ConnectionFolder> byId = [];
		foreach (ConnectionFolder folder in catalog.Folders)
		{
			byId.TryAdd(folder.Id, folder);
		}

		Dictionary<Guid, string> paths = [];
		List<string> names = [];
		HashSet<Guid> seen = [];
		foreach (ConnectionFolder folder in byId.Values)
		{
			names.Clear();
			seen.Clear();
			ConnectionFolder? current = folder;
			while (current is not null && seen.Add(current.Id))
			{
				names.Add(current.Name);
				current = current.ParentId is { } parentId && byId.TryGetValue(parentId, out ConnectionFolder? parent) ? parent : null;
			}

			names.Reverse();
			paths[folder.Id] = string.Join(" / ", names);
		}

		return paths;
	}

	/// <summary>Folder ids ordered by path, for pickers and move menus.</summary>
	public static IEnumerable<KeyValuePair<Guid, string>> Sorted(Dictionary<Guid, string> paths) =>
		paths.OrderBy(pair => pair.Value, StringComparer.CurrentCultureIgnoreCase);
}
