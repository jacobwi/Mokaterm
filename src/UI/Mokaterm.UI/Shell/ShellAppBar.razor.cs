using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Red.ContextMenu;
using Moka.Red.Feedback.CommandPalette;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.UI.Shell;

/// <summary>
/// Top bar: panel toggle, brand, quick connect, and the app tools menu (only while a module added one), palette,
/// transfers, lock and settings buttons.
/// </summary>
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

	[Inject]
	private IUiContributions Contributions { get; set; } = default!;

	[Inject]
	private IMokaContextMenuService Menu { get; set; } = default!;

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

	private void ShowToolsMenu(MouseEventArgs args)
	{
		List<MokaContextMenuItem> items = [];
		foreach (AppToolDescriptor tool in Contributions.AppTools)
		{
			AppToolDescriptor captured = tool;
			items.Add(new MokaContextMenuItem
			{
				Text = tool.Title,
				Icon = tool.Icon,
				Checked = ReferenceEquals(Shell.AppTool, tool),
				Shortcut = ShellShortcuts.LabelFor(ShellCommandIds.ForAppTool(tool)),
				OnClickSync = () => Shell.OpenAppTool(captured),
			});
		}

		Menu.Show(args, items);
	}

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
