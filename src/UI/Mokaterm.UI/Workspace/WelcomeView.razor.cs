using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.ContextMenu;
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
	private List<(HostProfile Host, ConnectionProfile Login)> _favorites = [];
	private List<(HostProfile Host, ConnectionProfile Login)> _recent = [];
	private int _restoreCount;
	private bool _hasHosts;

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
	private string RestoreText => _restoreCount == 1
		? "One session was open when Mokaterm last closed."
		: string.Create(CultureInfo.CurrentCulture, $"{_restoreCount} sessions were open when Mokaterm last closed.");

	private string Lede => _hasHosts
		? "Pick a login below, or start something new."
		: "Save a host to come back to, bring your hosts over from another client, or connect once without saving anything.";

	public void Dispose()
	{
		Catalog.Changed -= OnCatalogChanged;
		Settings.Changed -= OnSettingsChanged;
		Restore.Changed -= OnCatalogChanged;
	}

	protected override async Task OnInitializedAsync()
	{
		Catalog.Changed += OnCatalogChanged;
		Settings.Changed += OnSettingsChanged;
		Restore.Changed += OnCatalogChanged;
		Build();
		await Catalog.EnsureLoadedAsync();
		Build();
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

	private void Build()
	{
		ConnectionCatalog catalog = Catalog.Catalog;

		// A host with no login cannot be connected to, but its owner is past needing the getting-started screen.
		_hasHosts = catalog.Hosts.Count > 0;
		List<(HostProfile Host, ConnectionProfile Login)> logins = [];
		foreach (ConnectionProfile login in catalog.Connections)
		{
			if (catalog.FindHost(login.HostId) is { } host)
			{
				logins.Add((host, login));
			}
		}

		_favorites =
		[
			.. logins
				.Where(entry => entry.Login.IsFavorite)
				.OrderBy(entry => entry.Login.GetTitle(entry.Host), StringComparer.CurrentCultureIgnoreCase),
		];

		// A login deleted since the last run is not worth offering, so the count only covers ones still in the vault.
		HashSet<Guid> known = [.. catalog.Connections.Select(login => login.Id)];
		_restoreCount = Restore.Pending.Count(known.Contains);

		int limit = Math.Max(0, Settings.Get<GeneralSettings>().RecentConnectionsLimit);
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

	/// <summary>The recent list's length is a general setting, and the keys on every row are the shortcut bindings.</summary>
	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey == GeneralSettings.SectionKey || sectionKey == ShortcutSettings.SectionKey)
		{
			OnCatalogChanged();
		}
	}
}
