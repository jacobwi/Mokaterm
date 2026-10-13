using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.Connections;

namespace Mokaterm.UI.Shell;

/// <summary>
/// The unlocked application: app bar, connections panel, transfers panel, workspace, status bar and editors. Mounted
/// only while the vault is unlocked, so locking removes every piece of decrypted data from the DOM.
/// </summary>
public sealed partial class ShellLayout : ComponentBase, IAsyncDisposable
{
	private bool _showStatusBar;
	private int _lastActiveTransfers;

	[Inject]
	private ShellState Shell { get; set; } = default!;

	[Inject]
	private ShellCommandRegistry Commands { get; set; } = default!;

	[Inject]
	private ShellInputBridge InputBridge { get; set; } = default!;

	[Inject]
	private ConnectionActions Actions { get; set; } = default!;

	[Inject]
	private UiStateStore UiState { get; set; } = default!;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private ITransferQueue Transfers { get; set; } = default!;

	public async ValueTask DisposeAsync()
	{
		Shell.Changed -= OnShellChanged;
		Settings.Changed -= OnSettingsChanged;
		Transfers.Changed -= OnTransfersChanged;
		Commands.Unregister();
		await InputBridge.DetachAsync();
	}

	protected override void OnInitialized()
	{
		_showStatusBar = Settings.Get<AppearanceSettings>().ShowStatusBar;
		_lastActiveTransfers = Transfers.ActiveCount;
		Shell.Changed += OnShellChanged;
		Settings.Changed += OnSettingsChanged;
		Transfers.Changed += OnTransfersChanged;
		Commands.Register();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender)
		{
			await InputBridge.AttachAsync();
		}
	}

	private static string Px(double value) => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(value)}px");

	private void OnConnectionsPanelResized(double width) =>
		UiState.Update(state => state with { ConnectionsPanelWidth = width });

	private void OnTransfersPanelResized(double height) =>
		UiState.Update(state => state with { TransfersPanelHeight = height });

	private void OnShellChanged() => _ = InvokeAsync(StateHasChanged);

	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey == AppearanceSettings.SectionKey)
		{
			_ = InvokeAsync(() =>
			{
				_showStatusBar = Settings.Get<AppearanceSettings>().ShowStatusBar;
				StateHasChanged();
			});
		}
	}

	private void OnTransfersChanged()
	{
		int active = Transfers.ActiveCount;
		int previous = Interlocked.Exchange(ref _lastActiveTransfers, active);
		if (previous == 0 && active > 0)
		{
			_ = InvokeAsync(() => Shell.SetTransfersPanelCollapsed(false));
		}
	}
}
