using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Tools;

/// <summary>
/// The shell's side of tool windows, for session tools and app tools alike: the one rule for whether a tool opens in a
/// dialog or a window, the place and keep-on-top each tool's window is remembered with, and the windows this page
/// opened, which go when the vault locks or the page does. The host only manages the windows themselves
/// (<see cref="IToolWindows"/>); where it registers none, every tool stays in its dialog.
/// </summary>
internal sealed class ShellToolWindows : IDisposable
{
	private readonly ISettingsService _settings;
	private readonly UiStateStore _uiState;
	private readonly UserInteractionService _prompts;
	private readonly IVault _vault;
	private readonly IServiceProvider _services;
	private readonly IToolWindows? _windows;
	private readonly ILogger<ShellToolWindows> _logger;
	private InteractionRequest? _shownPrompt;
	private int _disposed;

	public ShellToolWindows(
		ISettingsService settings,
		UiStateStore uiState,
		UserInteractionService prompts,
		IVault vault,
		IServiceProvider services,
		ILogger<ShellToolWindows> logger)
	{
		_settings = settings;
		_uiState = uiState;
		_prompts = prompts;
		_vault = vault;
		_services = services;
		_windows = services.GetService<IToolWindows>();
		_logger = logger;

		_vault.StatusChanged += OnVaultStatusChanged;
		if (_windows is not null)
		{
			_windows.Changed += OnWindowsChanged;
			_prompts.Changed += OnPromptsChanged;
		}
	}

	/// <summary>Raised when a window opens or closes, or starts or stops staying on top. May fire on any thread.</summary>
	public event Action? Changed;

	/// <summary>A tool got a window of its own. May fire on any thread.</summary>
	public event Action<ToolWindowTarget>? Opened;

	/// <summary>
	/// The user closed a tool's window: its close button, or "Back to the main window". Not raised when the app closed
	/// it, which is what lets a window the app closed come back at the next start. May fire on any thread.
	/// </summary>
	public event Action<ToolWindowTarget>? ClosedByUser;

	/// <summary>Whether this host can put a tool in a window of its own at all.</summary>
	public bool CanOpenWindows => _windows is not null;

	/// <summary>Whether a tool opened now goes to a window rather than a dialog.</summary>
	public bool OpensWindows => _windows is not null && _settings.Get<AppearanceSettings>().ToolsInOwnWindow;

	public bool IsOpen(ToolWindowTarget target) => _windows?.IsOpen(target) == true;

	/// <summary>
	/// The rule every tool opens by: in a window when the host has them and the setting asks for one, or when the tool
	/// already has a window, which then comes to the front. False means the caller shows the tool in its dialog, which
	/// is also what happens when the window would not open.
	/// </summary>
	public Task<bool> ShowAsync(ToolWindowTarget target, string title, SessionToolSize size) =>
		OpensWindows || IsOpen(target) ? OpenAsync(target, title, size) : Task.FromResult(false);

	/// <summary>
	/// Opens the tool in a window whatever the setting says, for the dialog's pop-out button and a window coming back
	/// at start. Unless the settings say otherwise, it opens where that tool's window was last and stays on top if it did.
	/// </summary>
	public async Task<bool> OpenAsync(ToolWindowTarget target, string title, SessionToolSize size)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(title);
		if (_windows is null || Volatile.Read(ref _disposed) != 0)
		{
			return false;
		}

		string key = target.PlaceKey;
		bool remember = _settings.Get<AppearanceSettings>().RememberToolWindowPlaces;
		if (!remember && _uiState.Current.ToolWindows.Count > 0)
		{
			// Off means nothing about the windows is kept, places saved while it was on included.
			_uiState.Update(state => state with { ToolWindows = new Dictionary<string, ToolWindowBounds>(StringComparer.Ordinal) });
		}

		ToolWindowBounds? bounds = remember ? _uiState.Current.ToolWindows.GetValueOrDefault(key) : null;
		ToolWindowRequest request = new(
			target,
			title,
			_services,
			size,
			bounds is { IsUsable: true } ? bounds : null,
			remember ? moved => Remember(key, moved) : null)
		{
			ClosedByUser = () => ClosedByUser?.Invoke(target),
		};

		bool wasOpen = _windows.IsOpen(target);
		try
		{
			if (!await _windows.OpenAsync(request))
			{
				return false;
			}
		}
		catch (Exception ex)
		{
			// The dialog still works, so a window that would not open costs nothing but the window.
			_logger.LogWarning("A tool's window could not be opened: {Failure}", LogSafe.Describe(ex));
			return false;
		}

		if (!wasOpen)
		{
			Opened?.Invoke(target);
		}

		return true;
	}

	/// <summary>
	/// Closes a tool's window. <paramref name="byUser"/> says the user asked for it, so the window does not come back at
	/// the next start; otherwise the app closed it, the way it does when the tool's session goes away.
	/// </summary>
	public void Close(ToolWindowTarget target, bool byUser)
	{
		ArgumentNullException.ThrowIfNull(target);
		if (_windows is null || !_windows.IsOpen(target))
		{
			return;
		}

		if (byUser)
		{
			ClosedByUser?.Invoke(target);
		}

		_windows.Close(target);
	}

	/// <summary>Closes the windows of a session that went away. The app closed them, not the user.</summary>
	public void CloseSession(Guid sessionId) => _windows?.CloseSession(sessionId);

	public bool IsKeptOnTop(ToolWindowTarget target) => _windows?.IsKeptOnTop(target) == true;

	public void SetKeepOnTop(ToolWindowTarget target, bool keepOnTop) => _windows?.SetKeepOnTop(target, keepOnTop);

	/// <summary>Brings this page's window in front of its tool windows.</summary>
	public void ShowOwner() => _windows?.ShowOwner(_services);

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_vault.StatusChanged -= OnVaultStatusChanged;
		if (_windows is not null)
		{
			_windows.Changed -= OnWindowsChanged;
			_prompts.Changed -= OnPromptsChanged;

			// The windows borrow this scope, which is going away with the window that owns it.
			_windows.CloseAll(_services);
		}
	}

	private static Dictionary<string, ToolWindowBounds> WithBounds(
		IReadOnlyDictionary<string, ToolWindowBounds> current,
		string key,
		ToolWindowBounds bounds) =>
		new(current, StringComparer.Ordinal) { [key] = bounds };

	// Checked again when the window reports: the setting may have been turned off while it was open.
	private void Remember(string key, ToolWindowBounds bounds)
	{
		if (_settings.Get<AppearanceSettings>().RememberToolWindowPlaces)
		{
			_uiState.Update(state => state with { ToolWindows = WithBounds(state.ToolWindows, key, bounds) });
		}
	}

	private void OnVaultStatusChanged(VaultStatus status)
	{
		// Tools show hostnames, containers, files and whatever a plugin works on, none of which may stay on screen once
		// the vault locks. The app closes them, so they come back with the next unlock.
		if (status != VaultStatus.Unlocked)
		{
			_windows?.CloseAll(_services);
		}
	}

	private void OnWindowsChanged() => Changed?.Invoke();

	/// <summary>
	/// A question this window asks (a sudo password a tool's action needed, a host key on reconnect) shows here, not in
	/// a tool window, so this window comes forward once for each new one. Questions the tools ask themselves have their
	/// own queue in the tool's window and never reach this one.
	/// </summary>
	private void OnPromptsChanged()
	{
		InteractionRequest? current = _prompts.Current;
		if (current is null || ReferenceEquals(Interlocked.Exchange(ref _shownPrompt, current), current))
		{
			return;
		}

		_windows?.ShowOwner(_services);
	}
}
