using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Shell;

/// <summary>Bottom bar: sessions, the active session and its environment, transfers, vault state and app version.</summary>
public sealed partial class ShellStatusBar : ComponentBase, IDisposable
{
	private int _sessionCount;
	private ISessionHandle? _active;
	private (int Active, int Queued, int Failed, int Done) _transfers;

	[Inject]
	private SessionWorkspace Workspace { get; set; } = default!;

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

	private string TransfersText => _transfers switch
	{
		{ Active: > 0 } => _transfers.Queued > 0
			? string.Create(CultureInfo.CurrentCulture, $"{_transfers.Active} ACTIVE · {_transfers.Queued} QUEUED")
			: string.Create(CultureInfo.CurrentCulture, $"{_transfers.Active} ACTIVE"),
		{ Failed: > 0 } => string.Create(CultureInfo.CurrentCulture, $"{_transfers.Failed} FAILED"),
		{ Done: > 0 } => string.Create(CultureInfo.CurrentCulture, $"{_transfers.Done} DONE"),
		_ => "NO TRANSFERS",
	};

	private string VaultText
	{
		get
		{
			int minutes = Settings.Get<SecuritySettings>().AutoLockMinutes;
			return minutes > 0
				? string.Create(CultureInfo.CurrentCulture, $"VAULT UNLOCKED · AUTO-LOCK {minutes}M")
				: "VAULT UNLOCKED · AUTO-LOCK OFF";
		}
	}

	public void Dispose()
	{
		Workspace.Changed -= OnWorkspaceChanged;
		Transfers.Changed -= OnTransfersChanged;
		Settings.Changed -= OnSettingsChanged;
	}

	protected override void OnInitialized()
	{
		ReadSessions();
		_transfers = CountTransfers();
		Workspace.Changed += OnWorkspaceChanged;
		Transfers.Changed += OnTransfersChanged;
		Settings.Changed += OnSettingsChanged;
	}

	private void LockVault() => Vault.Lock(LockReason.User);

	private void ReadSessions()
	{
		IReadOnlyList<ISessionHandle> sessions = Workspace.Tabs;
		_sessionCount = sessions.Count;
		_active = Workspace.Active;
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
