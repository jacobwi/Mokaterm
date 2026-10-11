using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// Opens and closes the tools modules add to the session toolbar. A tool opens in a dialog over its session, or, where
/// the host has windows and the setting asks for them, in a window of its own. The rules for when a window has to go
/// are here rather than in the host: the session closed, or the vault locked, or this window's services went away.
/// </summary>
internal sealed class SessionToolActions : IDisposable
{
	private readonly IUiContributions _contributions;
	private readonly ISessionManager _sessions;
	private readonly IVault _vault;
	private readonly ISettingsService _settings;
	private readonly UiStateStore _uiState;
	private readonly UserInteractionService _prompts;
	private readonly IServiceProvider _services;
	private readonly IToolWindows? _windows;
	private readonly ILogger<SessionToolActions> _logger;
	private readonly Lock _gate = new();
	private readonly Dictionary<Guid, SessionToolDescriptor> _dialogs = [];
	private HashSet<Guid> _sessionIds;
	private InteractionRequest? _shownPrompt;
	private bool _disposed;

	public SessionToolActions(
		IUiContributions contributions,
		ISessionManager sessions,
		IVault vault,
		ISettingsService settings,
		UiStateStore uiState,
		UserInteractionService prompts,
		IServiceProvider services,
		ILogger<SessionToolActions> logger)
	{
		_contributions = contributions;
		_sessions = sessions;
		_vault = vault;
		_settings = settings;
		_uiState = uiState;
		_prompts = prompts;
		_services = services;
		_windows = services.GetService<IToolWindows>();
		_logger = logger;
		_sessionIds = [.. sessions.Sessions.Select(session => session.Id)];

		_sessions.SessionsChanged += OnSessionsChanged;
		_vault.StatusChanged += OnVaultStatusChanged;
		if (_windows is not null)
		{
			_windows.Changed += OnWindowsChanged;
			_prompts.Changed += OnPromptsChanged;
		}
	}

	/// <summary>Raised when a tool opens or closes, in a dialog or a window. May fire on any thread.</summary>
	public event Action? Changed;

	/// <summary>Whether this host can put a tool in a window of its own at all.</summary>
	public bool CanOpenWindows => _windows is not null;

	/// <summary>Whether a tool opened now goes to a window rather than a dialog.</summary>
	public bool OpensWindows => _windows is not null && _settings.Get<AppearanceSettings>().ToolsInOwnWindow;

	/// <summary>
	/// The title of a tool's window, such as "Docker · user@host": the plain middle dot, because a dash in a title reads
	/// as part of a host name.
	/// </summary>
	public static string WindowTitle(SessionToolDescriptor tool, ISessionHandle session)
	{
		ArgumentNullException.ThrowIfNull(tool);
		ArgumentNullException.ThrowIfNull(session);
		return $"{tool.Title} · {SessionTitle.Endpoint(session)}";
	}

	/// <summary>The tools the session's protocol has, whatever state the session is in.</summary>
	public IReadOnlyList<SessionToolDescriptor> For(ISessionHandle session)
	{
		ArgumentNullException.ThrowIfNull(session);
		return _contributions.FindSessionTools(session.Protocol.Id);
	}

	/// <summary>The tools the toolbar offers now: the ones that need the server wait until the session is connected.</summary>
	public IReadOnlyList<SessionToolDescriptor> Available(ISessionHandle session)
	{
		IReadOnlyList<SessionToolDescriptor> tools = For(session);
		return tools.Count == 0 || session.State == SessionState.Connected
			? tools
			: [.. tools.Where(tool => tool.ShowWhileDisconnected)];
	}

	/// <summary>The tool open in a dialog over the session. One that went away with the connection is not.</summary>
	public SessionToolDescriptor? DialogFor(ISessionHandle session)
	{
		ArgumentNullException.ThrowIfNull(session);
		SessionToolDescriptor? tool;
		lock (_gate)
		{
			tool = _dialogs.GetValueOrDefault(session.Id);
		}

		return tool is not null && Available(session).Contains(tool) ? tool : null;
	}

	public bool IsInWindow(ISessionHandle session, SessionToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(tool);
		return _windows?.IsOpen(session.Id, tool.Key) == true;
	}

	/// <summary>Whether the tool shows anywhere, in a dialog or in a window.</summary>
	public bool IsOpen(ISessionHandle session, SessionToolDescriptor tool) =>
		Equals(DialogFor(session), tool) || IsInWindow(session, tool);

	/// <summary>
	/// The toolbar's button: opens the tool, closes it again when it is open in a dialog, and brings its window to the
	/// front when it has one.
	/// </summary>
	public Task ToggleAsync(ISessionHandle session, SessionToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(session);
		if (Equals(DialogFor(session), tool))
		{
			CloseDialog(session.Id);
			return Task.CompletedTask;
		}

		return OpenAsync(session, tool);
	}

	/// <summary>Shows the tool, in a window when the setting asks for one and the host has them, else in a dialog.</summary>
	public async Task OpenAsync(ISessionHandle session, SessionToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(tool);
		if (!Available(session).Contains(tool))
		{
			_prompts.Notify(NoticeSeverity.Info, $"{tool.Title} needs the session to be connected.");
			return;
		}

		if ((OpensWindows || IsInWindow(session, tool)) && await OpenWindowAsync(session, tool))
		{
			return;
		}

		ShowDialog(session.Id, tool);
	}

	/// <summary>Moves a tool from its dialog to a window of its own. The dialog stays when no window could open.</summary>
	public async Task PopOutAsync(ISessionHandle session, SessionToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(tool);
		if (await OpenWindowAsync(session, tool))
		{
			CloseDialog(session.Id);
		}
	}

	public void CloseDialog(Guid sessionId)
	{
		bool removed;
		lock (_gate)
		{
			removed = _dialogs.Remove(sessionId);
		}

		if (removed)
		{
			Changed?.Invoke();
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_dialogs.Clear();
		}

		_sessions.SessionsChanged -= OnSessionsChanged;
		_vault.StatusChanged -= OnVaultStatusChanged;
		if (_windows is not null)
		{
			_windows.Changed -= OnWindowsChanged;
			_prompts.Changed -= OnPromptsChanged;

			// The windows borrow this scope, which is going away with the window that owns it.
			_windows.CloseAll(_services);
		}
	}

	private void ShowDialog(Guid sessionId, SessionToolDescriptor tool)
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_dialogs[sessionId] = tool;
		}

		Changed?.Invoke();
	}

	private async Task<bool> OpenWindowAsync(ISessionHandle session, SessionToolDescriptor tool)
	{
		if (_windows is null)
		{
			return false;
		}

		string key = tool.Key;
		ToolWindowBounds? bounds = _uiState.Current.ToolWindows.GetValueOrDefault(key);
		ToolWindowRequest request = new(
			session.Id,
			key,
			WindowTitle(tool, session),
			_services,
			tool.Size,
			bounds is { IsUsable: true } ? bounds : null,
			moved => _uiState.Update(state => state with { ToolWindows = WithBounds(state.ToolWindows, key, moved) }));
		try
		{
			return await _windows.OpenAsync(request);
		}
		catch (Exception ex)
		{
			// The dialog still works, so a window that would not open costs nothing but the window.
			_logger.LogWarning("A session tool's window could not be opened: {Failure}", LogSafe.Describe(ex));
			return false;
		}
	}

	private static Dictionary<string, ToolWindowBounds> WithBounds(
		IReadOnlyDictionary<string, ToolWindowBounds> current,
		string key,
		ToolWindowBounds bounds) =>
		new(current, StringComparer.Ordinal) { [key] = bounds };

	private void OnSessionsChanged()
	{
		HashSet<Guid> now = [.. _sessions.Sessions.Select(session => session.Id)];
		List<Guid> gone;
		bool dialogsChanged = false;
		lock (_gate)
		{
			gone = [.. _sessionIds.Where(id => !now.Contains(id))];
			_sessionIds = now;
			foreach (Guid id in gone)
			{
				dialogsChanged |= _dialogs.Remove(id);
			}
		}

		foreach (Guid id in gone)
		{
			_windows?.Close(id);
		}

		if (dialogsChanged)
		{
			Changed?.Invoke();
		}
	}

	private void OnVaultStatusChanged(VaultStatus status)
	{
		if (status == VaultStatus.Unlocked)
		{
			return;
		}

		// Tools show hostnames, containers and files, none of which may stay on screen once the vault locks.
		lock (_gate)
		{
			_dialogs.Clear();
		}

		_windows?.CloseAll(_services);
		Changed?.Invoke();
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
