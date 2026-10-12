using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.UI.Shell;

/// <summary>
/// Shell UI state for one scope: panels, the settings view, overlays and the open editor. It outlives vault locks, so
/// the layout comes back as it was after unlocking. Use it from the renderer's dispatcher only.
/// </summary>
internal sealed class ShellState
{
	private readonly UiStateStore _uiState;
	private bool? _connectionsPanelCollapsed;

	public ShellState(UiStateStore uiState) => _uiState = uiState;

	public event Action? Changed;

	public event Action? QuickConnectFocusRequested;

	// Read lazily: this service can be created before the UI state document has been loaded.
	public bool ConnectionsPanelCollapsed => _connectionsPanelCollapsed ?? _uiState.Current.ConnectionsPanelCollapsed;

	public bool TransfersPanelCollapsed { get; private set; } = true;

	public bool SettingsOpen { get; private set; }

	public string? SettingsPageId { get; private set; }

	public bool ShortcutsOpen { get; private set; }

	/// <summary>The editor dialog on screen, or null.</summary>
	public ShellEditorRequest? Editor { get; private set; }

	/// <summary>The app tool on screen, or null.</summary>
	public AppToolDescriptor? AppTool { get; private set; }

	public void SetConnectionsPanelCollapsed(bool collapsed)
	{
		if (ConnectionsPanelCollapsed == collapsed)
		{
			return;
		}

		_connectionsPanelCollapsed = collapsed;
		_uiState.Update(state => state with { ConnectionsPanelCollapsed = collapsed });
		Changed?.Invoke();
	}

	public void ToggleConnectionsPanel() => SetConnectionsPanelCollapsed(!ConnectionsPanelCollapsed);

	public void SetTransfersPanelCollapsed(bool collapsed)
	{
		if (TransfersPanelCollapsed == collapsed)
		{
			return;
		}

		TransfersPanelCollapsed = collapsed;
		Changed?.Invoke();
	}

	public void ToggleTransfersPanel() => SetTransfersPanelCollapsed(!TransfersPanelCollapsed);

	/// <summary>Shows the settings view over the workspace, optionally on <paramref name="pageId"/>.</summary>
	public void OpenSettings(string? pageId = null)
	{
		if (SettingsOpen && (pageId is null || pageId == SettingsPageId))
		{
			return;
		}

		SettingsOpen = true;
		SettingsPageId = pageId ?? SettingsPageId;
		Changed?.Invoke();
	}

	public void SelectSettingsPage(string pageId)
	{
		if (SettingsPageId == pageId)
		{
			return;
		}

		SettingsPageId = pageId;
		Changed?.Invoke();
	}

	public void CloseSettings()
	{
		if (!SettingsOpen)
		{
			return;
		}

		SettingsOpen = false;
		Changed?.Invoke();
	}

	public void SetShortcutsOpen(bool open)
	{
		if (ShortcutsOpen == open)
		{
			return;
		}

		ShortcutsOpen = open;
		Changed?.Invoke();
	}

	/// <summary>Shows an editor. Ignored while another editor is open so its unsaved input is never replaced.</summary>
	public bool OpenEditor(ShellEditorRequest request)
	{
		if (Editor is not null)
		{
			return false;
		}

		Editor = request;
		Changed?.Invoke();
		return true;
	}

	/// <summary>Closes <paramref name="request"/> if it is still the open editor.</summary>
	public void CloseEditor(ShellEditorRequest request)
	{
		if (!ReferenceEquals(Editor, request))
		{
			return;
		}

		Editor = null;
		Changed?.Invoke();
	}

	/// <summary>Shows an app tool, in place of the one on screen.</summary>
	public void OpenAppTool(AppToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(tool);
		if (ReferenceEquals(AppTool, tool))
		{
			return;
		}

		AppTool = tool;
		Changed?.Invoke();
	}

	public void CloseAppTool()
	{
		if (AppTool is null)
		{
			return;
		}

		AppTool = null;
		Changed?.Invoke();
	}

	/// <summary>Drops any open editor and app tool, for example when the vault locks and the shell unmounts.</summary>
	public void ResetTransientUi()
	{
		if (Editor is null && !ShortcutsOpen && AppTool is null)
		{
			return;
		}

		Editor = null;
		ShortcutsOpen = false;
		AppTool = null;
		Changed?.Invoke();
	}

	public void FocusQuickConnect() => QuickConnectFocusRequested?.Invoke();
}
