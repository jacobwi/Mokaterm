using Mokaterm.Abstractions.Security;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Tools;

/// <summary>
/// Opens and closes the tools that need no session, from the top bar's Tools menu and the palette. One shows in the
/// app tool dialog at a time; any number can have windows of their own, by the same rule session tools follow
/// (<see cref="ShellToolWindows"/>). A window lives until it is closed, and the dialog goes when the vault locks.
/// </summary>
internal sealed class AppToolActions : IDisposable
{
	private readonly IUiContributions _contributions;
	private readonly IVault _vault;
	private readonly ShellToolWindows _windows;
	private readonly Lock _gate = new();
	private AppToolDescriptor? _dialog;
	private bool _disposed;

	public AppToolActions(IUiContributions contributions, IVault vault, ShellToolWindows windows)
	{
		_contributions = contributions;
		_vault = vault;
		_windows = windows;
		_vault.StatusChanged += OnVaultStatusChanged;
		_windows.Changed += OnWindowsChanged;
	}

	/// <summary>Raised when a tool opens or closes, in the dialog or a window. May fire on any thread.</summary>
	public event Action? Changed;

	/// <summary>Whether this host can put a tool in a window of its own at all.</summary>
	public bool CanOpenWindows => _windows.CanOpenWindows;

	/// <summary>The tool in the dialog, or null.</summary>
	public AppToolDescriptor? Dialog
	{
		get
		{
			lock (_gate)
			{
				return _dialog;
			}
		}
	}

	/// <summary>Whether any app tool shows, in the dialog or a window, for the top bar's Tools button.</summary>
	public bool AnyOpen => Dialog is not null || _contributions.AppTools.Any(IsInWindow);

	/// <summary>The title of a tool's window, such as "Subnet calculator · Mokaterm", with the same dot session tools use.</summary>
	public static string WindowTitle(AppToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(tool);
		return $"{tool.Title} · Mokaterm";
	}

	/// <summary>What a window showing <paramref name="tool"/> is known by.</summary>
	public static ToolWindowTarget Target(AppToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(tool);
		return ToolWindowTarget.AppTool(tool.Id);
	}

	/// <summary>The app tool with <paramref name="id"/>, or null when no module registers it (any more).</summary>
	public AppToolDescriptor? Find(string id) =>
		_contributions.AppTools.FirstOrDefault(tool => string.Equals(tool.Id, id, StringComparison.Ordinal));

	public bool IsInWindow(AppToolDescriptor tool) => _windows.IsOpen(Target(tool));

	/// <summary>Whether the tool shows anywhere, in the dialog or a window.</summary>
	public bool IsOpen(AppToolDescriptor tool) => ReferenceEquals(Dialog, tool) || IsInWindow(tool);

	/// <summary>Shows the tool, in a window when the rule says so, else in the dialog in place of the one there.</summary>
	public async Task OpenAsync(AppToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(tool);
		if (await _windows.ShowAsync(Target(tool), WindowTitle(tool), tool.Size))
		{
			return;
		}

		ShowDialog(tool);
	}

	/// <summary>Moves the tool from the dialog to a window of its own. The dialog stays when no window could open.</summary>
	public async Task PopOutAsync(AppToolDescriptor tool)
	{
		if (await OpenWindowAsync(tool))
		{
			CloseDialog(tool);
		}
	}

	/// <summary>Opens the tool's window with no dialog to fall back to, for a window coming back after an unlock.</summary>
	public Task<bool> OpenWindowAsync(AppToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(tool);
		return _windows.OpenAsync(Target(tool), WindowTitle(tool), tool.Size);
	}

	/// <summary>
	/// "Back to the main window": the tool leaves its window for the dialog in the window that opened it, which comes to
	/// the front. The user closed that window, so it does not come back at the next start.
	/// </summary>
	public void ReturnToDialog(AppToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(tool);
		ShowDialog(tool);

		// Before the close: the owner only comes forward while it still has a tool window open.
		_windows.ShowOwner();
		_windows.Close(Target(tool), byUser: true);
	}

	/// <summary>Closes the dialog, whichever tool is in it.</summary>
	public void CloseDialog() => CloseDialog(null);

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_dialog = null;
		}

		_vault.StatusChanged -= OnVaultStatusChanged;
		_windows.Changed -= OnWindowsChanged;
	}

	private void ShowDialog(AppToolDescriptor tool)
	{
		lock (_gate)
		{
			if (_disposed || ReferenceEquals(_dialog, tool))
			{
				return;
			}

			_dialog = tool;
		}

		Changed?.Invoke();
	}

	// Only while the dialog still shows that tool, so a pop-out that finishes after another tool took the dialog leaves
	// that one alone.
	private void CloseDialog(AppToolDescriptor? only)
	{
		lock (_gate)
		{
			if (_dialog is null || (only is not null && !ReferenceEquals(_dialog, only)))
			{
				return;
			}

			_dialog = null;
		}

		Changed?.Invoke();
	}

	private void OnVaultStatusChanged(VaultStatus status)
	{
		// The windows go through ShellToolWindows; the dialog is this one's.
		if (status != VaultStatus.Unlocked)
		{
			CloseDialog();
		}
	}

	private void OnWindowsChanged() => Changed?.Invoke();
}
