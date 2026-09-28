using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Red.ContextMenu;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Connections;

/// <summary>
/// The connections tree: folders, hosts and their logins as a flattened list with filtering, keyboard navigation,
/// context menus and drag and drop of hosts between folders.
/// </summary>
public sealed partial class ConnectionExplorer : ComponentBase, IDisposable
{
	private const string RootDropKey = "root";
	private const int IndentPixels = 14;

	private readonly HashSet<Guid> _expanded = [];
	private ElementReference _tree;
	private List<ConnectionTreeRow> _rows = [];
	private Dictionary<Guid, SessionState> _sessionStates = [];
	private string _query = "";
	private string? _selectedKey;
	private Guid? _draggedHostId;
	private string? _dropTargetKey;
	private bool _scrollPending;

	[Inject]
	private CatalogState Catalog { get; set; } = default!;

	[Inject]
	private ConnectionActions Actions { get; set; } = default!;

	[Inject]
	private SessionActions Sessions { get; set; } = default!;

	[Inject]
	private SessionWorkspace Workspace { get; set; } = default!;

	[Inject]
	private UiStateStore UiState { get; set; } = default!;

	[Inject]
	private IProtocolRegistry Protocols { get; set; } = default!;

	[Inject]
	private IMokaContextMenuService Menu { get; set; } = default!;

	[Inject]
	private ShellInterop Interop { get; set; } = default!;

	private bool IsFiltering => _query.Trim().Length > 0;

	public void Dispose()
	{
		Catalog.Changed -= OnCatalogChanged;
		Workspace.Changed -= OnWorkspaceChanged;
	}

	protected override async Task OnInitializedAsync()
	{
		UiStateDocument layout = UiState.Current;
		_expanded.UnionWith(layout.ExpandedFolderIds);
		_expanded.UnionWith(layout.ExpandedHostIds);
		ReadSessionStates();
		Catalog.Changed += OnCatalogChanged;
		Workspace.Changed += OnWorkspaceChanged;
		Rebuild();
		await Catalog.EnsureLoadedAsync();
		Rebuild();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender)
		{
			await Interop.AttachListKeysAsync(_tree);
		}

		if (_scrollPending)
		{
			_scrollPending = false;
			await Interop.ScrollActiveIntoViewAsync(_tree);
		}
	}

	private static string IndentStyle(int depth) =>
		string.Create(CultureInfo.InvariantCulture, $"padding-left:calc(var(--moka-spacing-sm) + {depth * IndentPixels}px)");

	private static string? HostColorStyle(HostProfile host) => HexColor.Normalize(host.Color) is { } color ? $"color:{color}" : null;

	private static string LoginLabel(ConnectionProfile login) =>
		!string.IsNullOrWhiteSpace(login.Label) ? login.Label
		: !string.IsNullOrWhiteSpace(login.Username) ? login.Username
		: "user asked on connect";

	private static int Rank(SessionState state) => state switch
	{
		SessionState.Connected => 3,
		SessionState.Connecting => 2,
		SessionState.Failed => 1,
		_ => 0,
	};

	private string RowClass(ConnectionTreeRow row) =>
		row.Key == _dropTargetKey ? "mt-tree-item mt-tree-item--drop" : "mt-tree-item";

	private void Rebuild() => _rows = ConnectionTreeBuilder.Build(Catalog.Catalog, _expanded, _query, Protocols.DisplayName);

	private void ReadSessionStates()
	{
		Dictionary<Guid, SessionState> states = [];
		foreach (ISessionHandle session in Workspace.Tabs)
		{
			if (session.IsTransient)
			{
				continue;
			}

			Guid loginId = session.Connection.Id;
			if (!states.TryGetValue(loginId, out SessionState existing) || Rank(session.State) > Rank(existing))
			{
				states[loginId] = session.State;
			}
		}

		_sessionStates = states;
	}

	private void OnQueryChanged(string query)
	{
		_query = query;
		Rebuild();
	}

	private void Select(ConnectionTreeRow row) => _selectedKey = row.Key;

	private void SelectIndex(int index)
	{
		if (index >= 0 && index < _rows.Count)
		{
			_selectedKey = _rows[index].Key;
			_scrollPending = true;
		}
	}

	private async Task ActivateAsync(ConnectionTreeRow row)
	{
		_selectedKey = row.Key;
		switch (row)
		{
			case { Login: { } login }:
				await Sessions.OpenAsync(login.Id, reuseExisting: true);
				break;
			case { Host: { } host }:
				List<ConnectionProfile> logins = [.. Catalog.Catalog.ConnectionsOf(host.Id)];
				if (logins.Count == 1)
				{
					await Sessions.OpenAsync(logins[0].Id, reuseExisting: true);
				}
				else
				{
					Toggle(row);
				}

				break;
			default:
				Toggle(row);
				break;
		}
	}

	private void Toggle(ConnectionTreeRow row)
	{
		// While filtering every visible row is expanded, so there is nothing meaningful to toggle.
		if (!row.Expandable || IsFiltering)
		{
			return;
		}

		Guid? id = row.Folder?.Id ?? row.Host?.Id;
		if (id is not { } nodeId)
		{
			return;
		}

		if (!_expanded.Remove(nodeId))
		{
			_expanded.Add(nodeId);
		}

		SaveExpanded();
		Rebuild();
	}

	private void SaveExpanded()
	{
		ConnectionCatalog catalog = Catalog.Catalog;
		Guid[] folders = [.. _expanded.Where(id => catalog.FindFolder(id) is not null)];
		Guid[] hosts = [.. _expanded.Where(id => catalog.FindHost(id) is not null)];
		UiState.Update(state => state with { ExpandedFolderIds = folders, ExpandedHostIds = hosts });
	}

	private async Task OnKeyDownAsync(KeyboardEventArgs e)
	{
		if (_rows.Count == 0)
		{
			return;
		}

		int index = _rows.FindIndex(row => row.Key == _selectedKey);
		ConnectionTreeRow? current = index >= 0 ? _rows[index] : null;
		switch (e.Key)
		{
			case "ArrowDown":
				SelectIndex(index < 0 ? 0 : Math.Min(index + 1, _rows.Count - 1));
				break;
			case "ArrowUp":
				SelectIndex(index < 0 ? 0 : Math.Max(index - 1, 0));
				break;
			case "Home":
				SelectIndex(0);
				break;
			case "End":
				SelectIndex(_rows.Count - 1);
				break;
			case "ArrowRight" when current is { Expandable: true, Expanded: false }:
				Toggle(current);
				break;
			case "ArrowRight" when current is { Expandable: true, Expanded: true }:
				SelectIndex(index + 1);
				break;
			case "ArrowLeft" when current is { Expandable: true, Expanded: true } && !IsFiltering:
				Toggle(current);
				break;
			case "ArrowLeft" when current is not null:
				SelectParent(index);
				break;
			case "Enter" or " " when current is not null:
				await ActivateAsync(current);
				break;
			case "F2" when current is not null:
				Edit(current);
				break;
			case "Delete" when current is not null:
				await DeleteAsync(current);
				break;
		}
	}

	private void SelectParent(int index)
	{
		int depth = _rows[index].Depth;
		for (int i = index - 1; i >= 0; i--)
		{
			if (_rows[i].Depth < depth)
			{
				SelectIndex(i);
				return;
			}
		}
	}

	private void Edit(ConnectionTreeRow row)
	{
		switch (row)
		{
			case { Folder: { } folder }:
				Actions.RenameFolder(folder.Id);
				break;
			case { Login: { } login }:
				Actions.EditLogin(login.Id);
				break;
			case { Host: { } host }:
				Actions.EditHost(host.Id);
				break;
		}
	}

	private Task DeleteAsync(ConnectionTreeRow row) => row switch
	{
		{ Folder: { } folder } => Actions.DeleteFolderAsync(folder),
		{ Login: { } login, Host: { } host } => Actions.DeleteLoginAsync(host, login),
		{ Host: { } host } => Actions.DeleteHostAsync(host),
		_ => Task.CompletedTask,
	};

	private void OnRowContextMenu(MouseEventArgs e, ConnectionTreeRow row)
	{
		_selectedKey = row.Key;
		IReadOnlyList<MokaContextMenuItem> items = row switch
		{
			{ Folder: { } folder } => Actions.FolderMenu(folder),
			{ Login: { } login, Host: { } host } => Actions.LoginMenu(host, login),
			{ Host: { } host } => Actions.HostMenu(host),
			_ => [],
		};

		if (items.Count > 0)
		{
			Menu.Show(e, items);
		}
	}

	private void OnEmptyAreaContextMenu(MouseEventArgs e) => Menu.Show(e, Actions.EmptyAreaMenu());

	private void OnDragStart(ConnectionTreeRow row) =>
		_draggedHostId = row is { Kind: ConnectionTreeRowKind.Host, Host: { } host } ? host.Id : null;

	private void OnDragEnd()
	{
		_draggedHostId = null;
		_dropTargetKey = null;
	}

	private void OnDragEnter(string key)
	{
		if (_draggedHostId is not null)
		{
			_dropTargetKey = key;
		}
	}

	private async Task OnDropAsync(ConnectionTreeRow row)
	{
		if (_draggedHostId is not { } hostId)
		{
			return;
		}

		// Dropping on a host or login means "next to it": into the same folder.
		Guid? folderId = row switch
		{
			{ Folder: { } folder } => folder.Id,
			{ Host.FolderId: { } hostFolderId } when Catalog.Catalog.FindFolder(hostFolderId) is not null => hostFolderId,
			_ => null,
		};

		OnDragEnd();
		await Actions.MoveHostAsync(hostId, folderId);
		if (folderId is { } targetFolder && !IsFiltering && _expanded.Add(targetFolder))
		{
			SaveExpanded();
			Rebuild();
		}
	}

	private async Task OnDropOnRootAsync()
	{
		if (_draggedHostId is not { } hostId)
		{
			return;
		}

		OnDragEnd();
		await Actions.MoveHostAsync(hostId, null);
	}

	private void OnCatalogChanged() => _ = InvokeAsync(() =>
	{
		Rebuild();
		StateHasChanged();
	});

	private void OnWorkspaceChanged() => _ = InvokeAsync(() =>
	{
		ReadSessionStates();
		StateHasChanged();
	});
}
