using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Moka.Red.ContextMenu;
using Moka.Red.Core.Theming;
using Moka.Red.Feedback.CommandPalette;
using Moka.Red.Feedback.Dialog;
using Moka.Red.Feedback.Toast;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Vault;

namespace Mokaterm.UI;

/// <summary>
/// The whole Mokaterm UI as one component without parameters. Hosts render it as their root: loads settings and the
/// vault behind a boot screen, gates the shell behind the master password, and hosts the app-wide overlays.
/// </summary>
public sealed partial class MokatermApp : ComponentBase, IDisposable
{
	private readonly CancellationTokenSource _disposeCts = new();
	private MokaTheme _theme = MokaTheme.Dark;
	private bool _booted;
	private bool _booting;
	private string _bootStatus = "starting";
	private string? _bootError;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private IVault Vault { get; set; } = default!;

	[Inject]
	private UiStateStore UiState { get; set; } = default!;

	[Inject]
	private ShellState Shell { get; set; } = default!;

	[Inject]
	private IMokaToastService Toasts { get; set; } = default!;

	[Inject]
	private IMokaContextMenuService ContextMenu { get; set; } = default!;

	[Inject]
	private IMokaCommandPaletteService CommandPalette { get; set; } = default!;

	[Inject]
	private IMokaDialogService Dialogs { get; set; } = default!;

	[Inject]
	private DamagedDocumentNotices DamagedDocuments { get; set; } = default!;

	[Inject]
	private ILogger<MokatermApp> Logger { get; set; } = default!;

	public void Dispose()
	{
		Settings.Changed -= OnSettingsChanged;
		Vault.StatusChanged -= OnVaultStatusChanged;
		_disposeCts.Cancel();
		_disposeCts.Dispose();
	}

	protected override void OnInitialized()
	{
		Settings.Changed += OnSettingsChanged;
		Vault.StatusChanged += OnVaultStatusChanged;
		DamagedDocuments.Start();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender)
		{
			await BootAsync();
		}
	}

	private async Task BootAsync()
	{
		if (_booting || _booted)
		{
			return;
		}

		_booting = true;
		_bootError = null;
		try
		{
			SetBootStatus("loading settings");
			await Settings.LoadAsync(_disposeCts.Token);
			_theme = ThemeFactory.Create(Settings.Get<AppearanceSettings>());

			SetBootStatus("reading vault");
			await Vault.LoadAsync(_disposeCts.Token);
			await UiState.LoadAsync(_disposeCts.Token);
			_booted = true;
		}
		catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
		{
			return;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Mokaterm failed to start.");
			_bootError = ex.Message;
		}
		finally
		{
			_booting = false;
		}

		StateHasChanged();
	}

	private void SetBootStatus(string status)
	{
		_bootStatus = status;
		StateHasChanged();
	}

	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey != AppearanceSettings.SectionKey)
		{
			return;
		}

		_ = InvokeAsync(() =>
		{
			_theme = ThemeFactory.Create(Settings.Get<AppearanceSettings>());
			StateHasChanged();
		});
	}

	private void OnVaultStatusChanged(VaultStatus status) => _ = InvokeAsync(() =>
	{
		if (status != VaultStatus.Unlocked)
		{
			// Menus, the palette, toasts and editors can all show hostnames; none of them may outlive the unlocked shell.
			ContextMenu.Close();
			CommandPalette.Close();
			Toasts.Clear();

			// Service dialogs render above the shell: the file browser's delete and properties dialogs would stay on the
			// lock screen with remote paths in them, and could still be confirmed there.
			CloseServiceDialogs();
			Shell.ResetTransientUi();
		}

		StateHasChanged();
	});

	private void CloseServiceDialogs() => Dialogs.CloseAll();
}
