using Mokaterm.Abstractions.Connections;

namespace Mokaterm.UI.Connections;

/// <summary>
/// Flattens the catalog into the explorer's visible rows: at each level folders first, then hosts, each host followed
/// by its logins. A filter shows matching hosts and logins with their folders expanded.
/// </summary>
internal static class ConnectionTreeBuilder
{
	public static string FolderKey(Guid id) => "f:" + id.ToString("N");

	public static string HostKey(Guid id) => "h:" + id.ToString("N");

	public static string LoginKey(Guid id) => "l:" + id.ToString("N");

	/// <param name="catalog">The catalog to show.</param>
	/// <param name="expanded">Expanded folder and host ids. Ignored while filtering, when everything shown is expanded.</param>
	/// <param name="query">Filter text matched against host names, addresses, tags, usernames, labels and protocols.</param>
	/// <param name="protocolName">Display name for a protocol id, so "SSH" matches as well as "ssh".</param>
	public static List<ConnectionTreeRow> Build(
		ConnectionCatalog catalog,
		IReadOnlySet<Guid> expanded,
		string? query,
		Func<string, string> protocolName)
	{
		TreeWalker walker = new(catalog, expanded, string.IsNullOrWhiteSpace(query) ? null : query.Trim(), protocolName);
		walker.Visit(null, 0);

		// Folders in a parent cycle cannot be reached from the top level; show them there instead of losing them.
		HashSet<Guid> reachable = walker.FindReachableFolders();
		foreach (ConnectionFolder folder in walker.Folders)
		{
			if (!reachable.Contains(folder.Id))
			{
				walker.VisitFolder(folder, 0);
			}
		}

		return walker.Rows;
	}

	private sealed class TreeWalker
	{
		private readonly IReadOnlySet<Guid> _expanded;
		private readonly string? _filter;
		private readonly Func<string, string> _protocolName;
		private readonly ILookup<Guid?, ConnectionFolder> _foldersByParent;
		private readonly ILookup<Guid?, HostProfile> _hostsByFolder;
		private readonly ILookup<Guid, ConnectionProfile> _loginsByHost;
		private readonly HashSet<Guid> _visited = [];
		private readonly HashSet<Guid>? _visibleFolders;
		private readonly Dictionary<Guid, List<ConnectionProfile>>? _visibleLogins;

		public TreeWalker(ConnectionCatalog catalog, IReadOnlySet<Guid> expanded, string? filter, Func<string, string> protocolName)
		{
			_expanded = expanded;
			_filter = filter;
			_protocolName = protocolName;

			Dictionary<Guid, ConnectionFolder> foldersById = [];
			foreach (ConnectionFolder folder in catalog.Folders)
			{
				foldersById.TryAdd(folder.Id, folder);
			}

			Folders =
			[
				.. catalog.Folders
					.OrderBy(folder => folder.Order)
					.ThenBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase),
			];

			// Parents and folders that point at something missing fall back to the top level.
			_foldersByParent = Folders.ToLookup(folder =>
				folder.ParentId is { } parentId && parentId != folder.Id && foldersById.ContainsKey(parentId) ? parentId : (Guid?)null);
			_hostsByFolder = catalog.Hosts
				.OrderBy(host => host.DisplayName, StringComparer.CurrentCultureIgnoreCase)
				.ToLookup(host => host.FolderId is { } folderId && foldersById.ContainsKey(folderId) ? folderId : (Guid?)null);
			_loginsByHost = catalog.Connections
				.OrderBy(login => login.ProtocolId, StringComparer.Ordinal)
				.ThenBy(login => login.Username, StringComparer.CurrentCultureIgnoreCase)
				.ToLookup(login => login.HostId);

			if (filter is null)
			{
				return;
			}

			_visibleFolders = [];
			_visibleLogins = [];
			foreach (HostProfile host in catalog.Hosts)
			{
				bool hostMatches = HostMatches(host);
				List<ConnectionProfile> shown = hostMatches ? [.. _loginsByHost[host.Id]] : [.. _loginsByHost[host.Id].Where(LoginMatches)];
				if (!hostMatches && shown.Count == 0)
				{
					continue;
				}

				_visibleLogins[host.Id] = shown;
				Guid? folderId = host.FolderId;
				while (folderId is { } id && foldersById.TryGetValue(id, out ConnectionFolder? folder) && _visibleFolders.Add(id))
				{
					folderId = folder.ParentId;
				}
			}
		}

		public List<ConnectionFolder> Folders { get; }

		public List<ConnectionTreeRow> Rows { get; } = [];

		/// <summary>Folders reachable from the top level, whether or not they are expanded.</summary>
		public HashSet<Guid> FindReachableFolders()
		{
			HashSet<Guid> reachable = [];
			Stack<Guid?> pending = new([null]);
			while (pending.TryPop(out Guid? parentId))
			{
				foreach (ConnectionFolder folder in _foldersByParent[parentId])
				{
					if (reachable.Add(folder.Id))
					{
						pending.Push(folder.Id);
					}
				}
			}

			return reachable;
		}

		public void Visit(Guid? parentId, int depth)
		{
			foreach (ConnectionFolder folder in _foldersByParent[parentId])
			{
				VisitFolder(folder, depth);
			}

			foreach (HostProfile host in _hostsByFolder[parentId])
			{
				VisitHost(host, depth);
			}
		}

		public void VisitFolder(ConnectionFolder folder, int depth)
		{
			if (!_visited.Add(folder.Id) || (_visibleFolders is not null && !_visibleFolders.Contains(folder.Id)))
			{
				return;
			}

			int hostCount = _hostsByFolder[folder.Id].Count();
			bool hasChildren = hostCount > 0 || _foldersByParent[folder.Id].Any();
			bool expanded = hasChildren && (_filter is not null || _expanded.Contains(folder.Id));
			Rows.Add(new ConnectionTreeRow(ConnectionTreeRowKind.Folder, FolderKey(folder.Id), depth, hasChildren, expanded, Folder: folder, ChildCount: hostCount));
			if (expanded)
			{
				Visit(folder.Id, depth + 1);
			}
		}

		private void VisitHost(HostProfile host, int depth)
		{
			List<ConnectionProfile> logins;
			if (_visibleLogins is null)
			{
				logins = [.. _loginsByHost[host.Id]];
			}
			else if (_visibleLogins.TryGetValue(host.Id, out List<ConnectionProfile>? shown))
			{
				logins = shown;
			}
			else
			{
				return;
			}

			bool expanded = logins.Count > 0 && (_filter is not null || _expanded.Contains(host.Id));
			Rows.Add(new ConnectionTreeRow(ConnectionTreeRowKind.Host, HostKey(host.Id), depth, logins.Count > 0, expanded, Host: host, ChildCount: logins.Count));
			if (!expanded)
			{
				return;
			}

			foreach (ConnectionProfile login in logins)
			{
				Rows.Add(new ConnectionTreeRow(ConnectionTreeRowKind.Login, LoginKey(login.Id), depth + 1, false, false, Host: host, Login: login));
			}
		}

		private bool HostMatches(HostProfile host) =>
			Matches(host.Name) || Matches(host.Address) || host.Tags.Any(Matches);

		private bool LoginMatches(ConnectionProfile login) =>
			Matches(login.Username) || Matches(login.Label) || Matches(login.ProtocolId) || Matches(_protocolName(login.ProtocolId));

		private bool Matches(string? value) =>
			_filter is not null && value is not null && value.Contains(_filter, StringComparison.CurrentCultureIgnoreCase);
	}
}
