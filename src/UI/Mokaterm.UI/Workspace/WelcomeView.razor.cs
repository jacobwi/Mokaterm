using Moka.Red.ContextMenu;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// Shown when no session is open. A vault with no host in it gets the three ways to fill it, one of which is the
/// importer; a vault with hosts gets its favorites, its recent logins and the same actions without the explanations.
/// </summary>
public sealed partial class WelcomeView : ComponentBase, IDisposable
{
	private const int RestoreNamesShown = 4;

	private readonly string _idPrefix = $"mt-welcome-{Guid.NewGuid():N}";
	private List<(HostProfile Host, ConnectionProfile Login)> _favorites = [];
	private int _favoritesTotal;
	private List<(HostProfile Host, ConnectionProfile Login)> _recent = [];
	private List<(HostProfile Host, ConnectionProfile Login)> _restore = [];
	private bool _hasHosts;
	private int _hostCount;
	private int _loginCount;
	private int _folderCount;

	[Inject]
	private CatalogState Catalog { get; set; } = default!;

	[Inject]
	private SessionActions Sessions { get; set; } = default!;

	[Inject]
	private SessionRestore Restore { get; set; } = default!;

	[Inject]
	private ConnectionActions Connections { get; set; } = default!;

	[Inject]
	private ShellState Shell { get; set; } = default!;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private IProtocolRegistry Protocols { get; set; } = default!;

	[Inject]
	private IUiContributions Contributions { get; set; } = default!;

	[Inject]
	private IMokaContextMenuService Menu { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	/// <summary>The offer to reopen what was open last time, only for logins this vault still holds.</summary>
	private string RestoreText => _restore.Count == 1
		? "One session was open when Mokaterm last closed."
		: string.Create(CultureInfo.CurrentCulture, $"{_restore.Count} sessions were open when Mokaterm last closed.");

	private string Lede => _hasHosts
		? "Pick a login below, or start something new."
		: "Save a host to come back to, bring your hosts over from another client, or connect once without saving anything.";

	private string? Summary
	{
		get
		{
			// An empty vault says so in its title.
			if (!_hasHosts)
			{
				return null;
			}

			List<string> parts = [Count(_hostCount, "host", "hosts"), Count(_loginCount, "login", "logins")];
			if (_folderCount > 0)
			{
				parts.Add(Count(_folderCount, "folder", "folders"));
			}

			return string.Join(" · ", parts);
		}
	}

	public void Dispose()
	{
		Catalog.Changed -= OnCatalogChanged;
		Settings.Changed -= OnSettingsChanged;
		Restore.Changed -= OnCatalogChanged;
		Shell.Changed -= OnShellChanged;
	}

	protected override async Task OnInitializedAsync()
	{
		Catalog.Changed += OnCatalogChanged;
		Settings.Changed += OnSettingsChanged;
		Restore.Changed += OnCatalogChanged;
		Shell.Changed += OnShellChanged;
		Build();
		await Catalog.EnsureLoadedAsync();
		Build();
	}

	private static string Count(int count, string one, string many) =>
		string.Create(CultureInfo.CurrentCulture, $"{count} {(count == 1 ? one : many)}");

	private string SectionId(string section) => $"{_idPrefix}-{section}";

	// A login that names its protocol's own port says nothing more than one that leaves it empty.
	private int? PortOf(ConnectionProfile login)
	{
		if (login.Port is not { } port)
		{
			return null;
		}

		return Protocols.FindDescriptor(login.ProtocolId) is { } protocol && (!protocol.UsesPort || protocol.DefaultPort == port)
			? null
			: port;
	}

	private async Task ReopenAsync()
	{
		await Restore.RestoreAsync();
		Build();
	}

	private void DismissRestore()
	{
		Restore.Dismiss();
		Build();
	}

	private void ShowConnections() => Shell.SetConnectionsPanelCollapsed(false);

	private void Build()
	{
		ConnectionCatalog catalog = Catalog.Catalog;

		// A host with no login cannot be connected to, but its owner is past needing the getting-started screen.
		_hasHosts = catalog.Hosts.Count > 0;
		_hostCount = catalog.Hosts.Count;
		_loginCount = catalog.Connections.Count;
		_folderCount = catalog.Folders.Count;

		List<(HostProfile Host, ConnectionProfile Login)> logins = [];
		Dictionary<Guid, (HostProfile Host, ConnectionProfile Login)> byId = new();
		foreach (ConnectionProfile login in catalog.Connections)
		{
			if (catalog.FindHost(login.HostId) is { } host)
			{
				logins.Add((host, login));
				byId[login.Id] = (host, login);
			}
		}

		// The same limit the Recent column takes: a vault of thirty favorites would otherwise stretch one column of the
		// grid far past its neighbours, and a star is only ever a click away in the connections panel.
		int limit = Math.Max(0, Settings.Get<GeneralSettings>().RecentConnectionsLimit);
		IEnumerable<(HostProfile Host, ConnectionProfile Login)> favorites =
			logins
				.Where(entry => entry.Login.IsFavorite)
				.OrderBy(entry => entry.Login.GetTitle(entry.Host), StringComparer.CurrentCultureIgnoreCase);
		_favoritesTotal = favorites.Count();
		_favorites = [.. favorites.Take(limit)];

		// A login deleted since the last run is not worth offering, so the offer only covers ones still in the vault.
		_restore = [.. Restore.Pending.Where(byId.ContainsKey).Select(id => byId[id])];

		_recent =
		[
			.. logins
				.Where(entry => entry.Login.LastConnectedAt is not null)
				.OrderByDescending(entry => entry.Login.LastConnectedAt)
				.Take(limit),
		];
	}

	private void OnCatalogChanged() => _ = InvokeAsync(() =>
	{
		Build();
		StateHasChanged();
	});

	// The hint about the connections panel offers to bring it back only while it is hidden.
	private void OnShellChanged() => _ = InvokeAsync(StateHasChanged);

	/// <summary>The recent list's length is a general setting, and the keys on every row are the shortcut bindings.</summary>
	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey == GeneralSettings.SectionKey || sectionKey == ShortcutSettings.SectionKey)
		{
			OnCatalogChanged();
		}
	}
}
