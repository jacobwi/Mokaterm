using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Shell;

/// <summary>
/// Bottom bar: what the vault holds, what is open, the active session with its machine and what its shell calls itself,
/// then transfers, vault state and the app version. Whether a session is connected is not here on purpose: the tab dot
/// and the session toolbar say that once.
/// </summary>
public sealed partial class ShellStatusBar : ComponentBase, IDisposable
{
	/// <summary>The settings page the version opens, as its descriptor registers it.</summary>
	private const string AboutPageId = "about";

	private int _sessionCount;
	private bool _isSplit;
	private ISessionHandle? _active;
	private string? _remoteTitle;
	private int _broadcastCount;
	private bool _typesWithOthers;
	private bool _catalogLoaded;
	private int _hosts;
	private int _logins;
	private (int Active, int Queued, int Failed, int Done) _transfers;

	[Inject]
	private SessionWorkspace Workspace { get; set; } = default!;

	[Inject]
	private InputBroadcast Broadcast { get; set; } = default!;

	[Inject]
	private CatalogState Catalog { get; set; } = default!;

	[Inject]
	private ITransferQueue Transfers { get; set; } = default!;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private IVault Vault { get; set; } = default!;

	[Inject]
	private ShellState Shell { get; set; } = default!;

	[Inject]
	private IAppEnvironment Environment { get; set; } = default!;

	/// <summary>A group of one sends nowhere, so the warning only shows once the active session has someone to type with.</summary>
	private bool ShowsBroadcast => _typesWithOthers && _broadcastCount > 1;

	private string HostsLabel => _hosts switch
	{
		0 => "NO HOSTS SAVED",
		1 => "HOST",
		_ => "HOSTS",
	};

	private string HostsTooltip => _hosts == 0
		? "Nothing is saved in this vault yet. Click to show or hide the connections panel."
		: string.Create(
			CultureInfo.CurrentCulture,
			$"{Plural(_hosts, "host")} and {Plural(_logins, "login")} saved. Click to show or hide the connections panel.");

	private string SessionsLabel => _sessionCount switch
	{
		0 => "NO SESSIONS",
		1 => "SESSION",
		_ => "SESSIONS",
	};

	private string SessionsTooltip
	{
		get
		{
			string open = _sessionCount switch
			{
				0 => "Nothing is open.",
				1 => "One session is open.",
				_ => string.Create(CultureInfo.CurrentCulture, $"{_sessionCount} sessions are open."),
			};

			return _isSplit ? open + " Both panes hold tabs." : open;
		}
	}

	private string BroadcastText => string.Create(CultureInfo.CurrentCulture, $"TYPING INTO {_broadcastCount}");

	private string BroadcastTooltip => string.Create(
		CultureInfo.CurrentCulture,
		$"What you type here also goes to {Plural(_broadcastCount - 1, "other session")}.");

	private string TransfersText => _transfers switch
	{
		{ Active: > 0 } => _transfers.Queued > 0
			? string.Create(CultureInfo.CurrentCulture, $"{_transfers.Active} ACTIVE · {_transfers.Queued} QUEUED")
			: string.Create(CultureInfo.CurrentCulture, $"{_transfers.Active} ACTIVE"),
		{ Failed: > 0 } => string.Create(CultureInfo.CurrentCulture, $"{_transfers.Failed} FAILED"),
		{ Done: > 0 } => string.Create(CultureInfo.CurrentCulture, $"{_transfers.Done} DONE"),
		_ => "NO TRANSFERS",
	};

	private string TransfersTooltip => _transfers switch
	{
		{ Active: > 0 } => "Transfers are running. Click to show or hide the transfers panel.",
		{ Failed: > 0 } => "A transfer failed. Click to show or hide the transfers panel.",
		_ => "Click to show or hide the transfers panel.",
	};

	private string VaultText => AutoLockMinutes > 0
		? string.Create(CultureInfo.CurrentCulture, $"UNLOCKED · AUTO-LOCK {AutoLockMinutes}M")
		: "UNLOCKED · NO AUTO-LOCK";

	private string VaultTooltip => AutoLockMinutes > 0
		? string.Create(
			CultureInfo.CurrentCulture,
			$"The vault locks by itself after {Plural(AutoLockMinutes, "minute")} without activity. Click to lock it now.")
		: "The vault never locks by itself. Click to lock it now.";

	private string VersionTooltip => string.Create(
		CultureInfo.CurrentCulture,
		$"Mokaterm {Environment.AppVersion} on {Environment.PlatformName}. Click for versions, modules and credits.");

	private int AutoLockMinutes => Settings.Get<SecuritySettings>().AutoLockMinutes;

	public void Dispose()
	{
		Workspace.Changed -= OnWorkspaceChanged;
		Broadcast.Changed -= OnBroadcastChanged;
		Catalog.Changed -= OnCatalogChanged;
		Transfers.Changed -= OnTransfersChanged;
		Settings.Changed -= OnSettingsChanged;
	}

	protected override async Task OnInitializedAsync()
	{
		ReadSessions();
		ReadCatalog();
		_transfers = CountTransfers();
		Workspace.Changed += OnWorkspaceChanged;
		Broadcast.Changed += OnBroadcastChanged;
		Catalog.Changed += OnCatalogChanged;
		Transfers.Changed += OnTransfersChanged;
		Settings.Changed += OnSettingsChanged;

		// The explorer normally has it loaded already; asking again costs nothing and shares that one load.
		await Catalog.EnsureLoadedAsync();
		ReadCatalog();
	}

	private static string ActiveTooltip(ISessionHandle session) =>
		string.Create(CultureInfo.CurrentCulture, $"{session.Protocol.DisplayName} to {EndpointFormat.Format(session)}");

	private static string EnvironmentTooltip(ISessionHandle session) =>
		string.Create(CultureInfo.CurrentCulture, $"Host environment: {HostEnvironmentDisplay.Name(session.Host.Environment)}");

	private static string RemoteTitleTooltip(string remoteTitle) =>
		string.Create(CultureInfo.CurrentCulture, $"What this session's shell calls itself: {remoteTitle}");

	/// <summary>"1 host" or "4 hosts", so no tooltip carries the plural rule of its own.</summary>
	private static string Plural(int count, string singular) =>
		string.Create(CultureInfo.CurrentCulture, $"{count} {(count == 1 ? singular : singular + "s")}");

	private void LockVault() => Vault.Lock(LockReason.User);

	private void OpenAbout() => Shell.OpenSettings(AboutPageId);

	private void ReadSessions()
	{
		_sessionCount = Workspace.Tabs.Count;
		_isSplit = Workspace.IsSplit;
		_active = Workspace.Active;
		_remoteTitle = _active is { } session ? Workspace.GetRemoteTitle(session.Id) : null;
		ReadBroadcast();
	}

	private void ReadBroadcast()
	{
		_broadcastCount = Broadcast.Count;
		_typesWithOthers = _active is { } session && Broadcast.IsMember(session.Id);
	}

	private void ReadCatalog()
	{
		_catalogLoaded = Catalog.IsLoaded;
		_hosts = Catalog.Catalog.Hosts.Count;
		_logins = Catalog.Catalog.Connections.Count;
	}

	private (int Active, int Queued, int Failed, int Done) CountTransfers()
	{
		int queued = 0;
		int failed = 0;
		int done = 0;
		foreach (ITransferItem item in Transfers.Items)
		{
			switch (item.State)
			{
				case TransferState.Queued:
					queued++;
					break;
				case TransferState.Failed:
					failed++;
					break;
				case TransferState.Completed:
					done++;
					break;
				default:
					break;
			}
		}

		return (Transfers.ActiveCount, queued, failed, done);
	}

	private void OnWorkspaceChanged() => _ = InvokeAsync(() =>
	{
		ReadSessions();
		StateHasChanged();
	});

	private void OnBroadcastChanged() => _ = InvokeAsync(() =>
	{
		ReadBroadcast();
		StateHasChanged();
	});

	private void OnCatalogChanged() => _ = InvokeAsync(() =>
	{
		ReadCatalog();
		StateHasChanged();
	});

	// Progress ticks also raise Changed; only render when the counts actually moved.
	private void OnTransfersChanged() => _ = InvokeAsync(() =>
	{
		(int Active, int Queued, int Failed, int Done) counts = CountTransfers();
		if (counts != _transfers)
		{
			_transfers = counts;
			StateHasChanged();
		}
	});

	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey == SecuritySettings.SectionKey)
		{
			_ = InvokeAsync(StateHasChanged);
		}
	}
}
