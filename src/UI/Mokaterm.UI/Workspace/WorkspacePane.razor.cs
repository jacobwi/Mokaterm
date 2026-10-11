using Microsoft.AspNetCore.Components;
using Moka.Red.ContextMenu;
using Moka.Red.Core.Interactions;
using Moka.Red.Navigation.Tabs.Models;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// One pane: its tab strip and the views of its tabs. Every view stays mounted and inactive ones are hidden, so
/// terminals and file browsers keep their state across tab switches and while the settings view is open.
/// </summary>
/// <remarks>
/// A pane follows the workspace itself rather than taking its tabs as a parameter. Its parameters are plain values, so a
/// split that re-renders, as Moka's does when a drag ends, leaves the pane and its views alone.
/// </remarks>
public sealed partial class WorkspacePane : ComponentBase, IDisposable
{
	private readonly Dictionary<Guid, TabInfo<Guid>> _tabInfos = [];
	private readonly List<TabInfo<Guid>> _tabs = [];
	private readonly List<ISessionHandle> _views = [];
	private Guid? _activeId;
	private bool _alone;
	private bool _disposed;

	/// <summary>The pane this shows, an id from <see cref="SessionWorkspace.Panes"/>.</summary>
	[Parameter]
	public int PaneId { get; set; }

	/// <summary>Marks the pane the commands act on. Only worth drawing while several panes are on screen.</summary>
	[Parameter]
	public bool Focused { get; set; }

	[Inject]
	private SessionWorkspace Workspace { get; set; } = default!;

	[Inject]
	private SessionActions Sessions { get; set; } = default!;

	[Inject]
	private ShellState Shell { get; set; } = default!;

	[Inject]
	private IMokaContextMenuService Menu { get; set; } = default!;

	[Inject]
	private MokaTabIconProvider IconProvider { get; set; } = default!;

	private string PaneClass => Focused ? "mt-workspace-pane mt-workspace-pane--focused" : "mt-workspace-pane";

	private string? ActiveTabId => Shell.SettingsOpen ? null : _activeId?.ToString("N");

	public void Dispose()
	{
		_disposed = true;
		Workspace.Changed -= OnWorkspaceChanged;
		Shell.Changed -= OnShellChanged;
	}

	protected override void OnInitialized()
	{
		Workspace.Changed += OnWorkspaceChanged;
		Shell.Changed += OnShellChanged;
	}

	protected override void OnParametersSet() => Rebuild();

	private static Guid? TryParseTabId(string tabId) => Guid.TryParseExact(tabId, "N", out Guid id) ? id : null;

	private void Rebuild()
	{
		_alone = Workspace.PaneCount == 1;
		_activeId = Workspace.ActiveIdIn(PaneId);
		_tabs.Clear();
		_views.Clear();

		HashSet<Guid> here = [];
		foreach (ISessionHandle session in Workspace.TabsIn(PaneId))
		{
			here.Add(session.Id);
			_tabs.Add(BuildTab(session));
		}

		// Views render in open order, so reordering tabs never moves a terminal's DOM.
		foreach (ISessionHandle session in Workspace.OpenOrder)
		{
			if (here.Contains(session.Id))
			{
				_views.Add(session);
			}
		}

		foreach (Guid gone in _tabInfos.Keys.Where(id => !here.Contains(id)).ToList())
		{
			_tabInfos.Remove(gone);
		}
	}

	private TabInfo<Guid> BuildTab(ISessionHandle session)
	{
		if (!_tabInfos.TryGetValue(session.Id, out TabInfo<Guid>? tab))
		{
			tab = new TabInfo<Guid>
			{
				Id = session.Id.ToString("N"),
				Value = session.Id,
				IconContent = TabIcon(session),
			};
			_tabInfos[session.Id] = tab;
		}

		bool production = session.Host.Environment == HostEnvironment.Production;

		// The whole label sits in the icon fragment; the strip's own title would only repeat it in one colour.
		tab.Title = "";
		tab.Tooltip = Workspace.GetRemoteTitle(session.Id) is { Length: > 0 } remoteTitle
			? $"{EndpointFormat.Format(session)}{Environment.NewLine}{remoteTitle}"
			: EndpointFormat.Format(session);
		tab.ActiveColor = HexColor.Normalize(session.Host.Color) ?? (production ? "var(--moka-color-error)" : null);
		tab.CssClass = production ? "mt-tab--production" : null;
		return tab;
	}

	private void FocusThisPane() => Workspace.FocusPane(PaneId);

	private void OnTabActivated(string tabId)
	{
		if (TryParseTabId(tabId) is { } id)
		{
			Shell.CloseSettings();
			Workspace.Activate(id);
		}
	}

	private void ShowTabMenu(MokaItemContextMenuArgs<TabInfo<Guid>> args)
	{
		if (Workspace.Find(args.Item.Value) is { } session)
		{
			Menu.Show(args.MouseEvent, Sessions.BuildTabMenu(session));
		}
	}

	private Task OnTabClosedAsync(string tabId) =>
		TryParseTabId(tabId) is { } id ? CloseTabAsync(id) : Task.CompletedTask;

	private Task CloseTabAsync(Guid sessionId) => Sessions.CloseAsync([sessionId]);

	private void OnTabReordered((string TabId, int NewIndex) move)
	{
		if (TryParseTabId(move.TabId) is { } id)
		{
			Workspace.Move(id, move.NewIndex);
		}
	}

	private void OnWorkspaceChanged() => _ = InvokeAsync(() =>
	{
		// A pane that lost its last tab is on its way out, and the change that removes it may still be queued here.
		if (_disposed)
		{
			return;
		}

		Rebuild();
		StateHasChanged();
	});

	private void OnShellChanged() => _ = InvokeAsync(() =>
	{
		if (!_disposed)
		{
			StateHasChanged();
		}
	});
}
