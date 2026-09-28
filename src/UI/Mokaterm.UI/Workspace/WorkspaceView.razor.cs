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
/// Session tabs and views, in one pane or two side by side. Every session view stays mounted and inactive ones are
/// hidden, so terminals and file browsers keep their state across tab switches and while the settings view is open.
/// </summary>
public sealed partial class WorkspaceView : ComponentBase, IDisposable
{
	private readonly Dictionary<Guid, TabInfo<Guid>> _tabInfos = [];
	private readonly List<TabInfo<Guid>>[] _tabs = [[], []];
	private readonly List<ISessionHandle>[] _views = [[], []];
	private readonly Guid?[] _activeIds = new Guid?[SessionWorkspace.PaneCount];
	private bool _split;
	private int _focusedPane;

	[Inject]
	private SessionWorkspace Workspace { get; set; } = default!;

	[Inject]
	private SessionActions Sessions { get; set; } = default!;

	[Inject]
	private SessionRestore Restore { get; set; } = default!;

	[Inject]
	private ShellState Shell { get; set; } = default!;

	[Inject]
	private IMokaContextMenuService Menu { get; set; } = default!;

	[Inject]
	private MokaTabIconProvider IconProvider { get; set; } = default!;

	public void Dispose()
	{
		Workspace.Changed -= OnWorkspaceChanged;
		Shell.Changed -= OnShellChanged;
	}

	protected override async Task OnInitializedAsync()
	{
		Rebuild();
		Workspace.Changed += OnWorkspaceChanged;
		Shell.Changed += OnShellChanged;

		// The shell renders only while the vault is unlocked, so this is the first moment a session can be opened.
		await Restore.StartAsync();
	}

	private static Guid? TryParseTabId(string tabId) => Guid.TryParseExact(tabId, "N", out Guid id) ? id : null;

	private string? ActiveTabId(int pane) => Shell.SettingsOpen ? null : _activeIds[pane]?.ToString("N");

	/// <summary>The focus ring only means something while both panes are on screen.</summary>
	private string PaneClass(int pane) =>
		_split && pane == _focusedPane ? "mt-workspace-pane mt-workspace-pane--focused" : "mt-workspace-pane";

	private void Rebuild()
	{
		_split = Workspace.IsSplit;
		_focusedPane = Workspace.FocusedPane;

		// With one pane on screen it is whichever pane holds the tabs, so closing the last tab of a pane never leaves
		// the window half empty.
		int solo = _split ? -1 : Workspace.TabsIn(1).Count > 0 && Workspace.TabsIn(0).Count == 0 ? 1 : 0;
		HashSet<Guid> open = [];
		for (int pane = 0; pane < SessionWorkspace.PaneCount; pane++)
		{
			int source = _split ? pane : pane == 0 ? solo : -1;
			_tabs[pane].Clear();
			_views[pane].Clear();
			_activeIds[pane] = source < 0 ? null : Workspace.ActiveIdIn(source);
			if (source < 0)
			{
				continue;
			}

			foreach (ISessionHandle session in Workspace.TabsIn(source))
			{
				open.Add(session.Id);
				_tabs[pane].Add(BuildTab(session));
			}

			// Views render in open order, so reordering tabs never moves a terminal's DOM.
			foreach (ISessionHandle session in Workspace.OpenOrder)
			{
				if (Workspace.PaneOf(session.Id) == source)
				{
					_views[pane].Add(session);
				}
			}
		}

		foreach (Guid closed in _tabInfos.Keys.Where(id => !open.Contains(id)).ToList())
		{
			_tabInfos.Remove(closed);
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
		Rebuild();
		StateHasChanged();
	});

	private void OnShellChanged() => _ = InvokeAsync(StateHasChanged);
}
