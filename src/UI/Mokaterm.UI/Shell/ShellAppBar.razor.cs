using Microsoft.AspNetCore.Components;
using Moka.Red.Feedback.CommandPalette;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Transfers;

namespace Mokaterm.UI.Shell;

/// <summary>Top bar: panel toggle, brand, quick connect, and the palette, transfers, lock and settings buttons.</summary>
public sealed partial class ShellAppBar : ComponentBase, IDisposable
{
	private int _activeTransfers;

	[Inject]
	private ShellState Shell { get; set; } = default!;

	[Inject]
	private IMokaCommandPaletteService Palette { get; set; } = default!;

	[Inject]
	private ITransferQueue Transfers { get; set; } = default!;

	[Inject]
	private IVault Vault { get; set; } = default!;

	public void Dispose()
	{
		Shell.Changed -= OnShellChanged;
		Transfers.Changed -= OnTransfersChanged;
	}

	protected override void OnInitialized()
	{
		_activeTransfers = Transfers.ActiveCount;
		Shell.Changed += OnShellChanged;
		Transfers.Changed += OnTransfersChanged;
	}

	private void LockVault() => Vault.Lock(LockReason.User);

	private void ToggleSettings()
	{
		if (Shell.SettingsOpen)
		{
			Shell.CloseSettings();
		}
		else
		{
			Shell.OpenSettings();
		}
	}

	private void OnShellChanged() => _ = InvokeAsync(StateHasChanged);

	// The queue raises Changed for progress too; only a different active count is worth a render here.
	private void OnTransfersChanged()
	{
		int active = Transfers.ActiveCount;
		if (Interlocked.Exchange(ref _activeTransfers, active) != active)
		{
			_ = InvokeAsync(StateHasChanged);
		}
	}
}
