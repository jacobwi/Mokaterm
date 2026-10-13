using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Tools;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// Opens and closes the tools modules add to the session toolbar. A tool opens in a dialog over its session, or in a
/// window of its own by the rule <see cref="ShellToolWindows"/> holds for every tool. The rules for when a session
/// tool has to go are here rather than in the host: its session closed, or the vault locked.
/// </summary>
internal sealed class SessionToolActions : IDisposable
{
	private readonly IUiContributions _contributions;
	private readonly ISessionManager _sessions;
	private readonly IVault _vault;
	private readonly ShellToolWindows _windows;
	private readonly ISessionTabs _tabs;
	private readonly UserInteractionService _prompts;
	private readonly Lock _gate = new();
	private readonly Dictionary<Guid, SessionToolDescriptor> _dialogs = [];
	private HashSet<Guid> _sessionIds;
	private bool _disposed;

	public SessionToolActions(
		IUiContributions contributions,
		ISessionManager sessions,
		IVault vault,
		ShellToolWindows windows,
		ISessionTabs tabs,
		UserInteractionService prompts)
	{
		_contributions = contributions;
		_sessions = sessions;
		_vault = vault;
		_windows = windows;
		_tabs = tabs;
		_prompts = prompts;
		_sessionIds = [.. sessions.Sessions.Select(session => session.Id)];

		_sessions.SessionsChanged += OnSessionsChanged;
		_vault.StatusChanged += OnVaultStatusChanged;
		_windows.Changed += OnWindowsChanged;
	}

	/// <summary>Raised when a tool opens or closes, in a dialog or a window. May fire on any thread.</summary>
	public event Action? Changed;

	/// <summary>Whether this host can put a tool in a window of its own at all.</summary>
	public bool CanOpenWindows => _windows.CanOpenWindows;

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

	/// <summary>What a window showing <paramref name="tool"/> on <paramref name="session"/> is known by.</summary>
	public static ToolWindowTarget Target(ISessionHandle session, SessionToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(tool);
		return ToolWindowTarget.SessionTool(session.Id, tool.Key);
	}

	/// <summary>The tools the session's protocol has, whatever state the session is in.</summary>
	public IReadOnlyList<SessionToolDescriptor> For(ISessionHandle session)
	{
		ArgumentNullException.ThrowIfNull(session);
		return _contributions.FindSessionTools(session.Protocol.Id);
	}

	/// <summary>The session's tool with <paramref name="key"/>, or null when its protocol has none.</summary>
	public SessionToolDescriptor? Find(ISessionHandle session, string key) =>
		For(session).FirstOrDefault(tool => string.Equals(tool.Key, key, StringComparison.Ordinal));

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

	public bool IsInWindow(ISessionHandle session, SessionToolDescriptor tool) => _windows.IsOpen(Target(session, tool));

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

	/// <summary>Shows the tool, in a window when the rule says so, else in a dialog.</summary>
	public async Task OpenAsync(ISessionHandle session, SessionToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(tool);
		if (!Available(session).Contains(tool))
		{
			_prompts.Notify(NoticeSeverity.Info, $"{tool.Title} needs the session to be connected.");
			return;
		}

		if (await _windows.ShowAsync(Target(session, tool), WindowTitle(tool, session), tool.Size))
		{
			return;
		}

		ShowDialog(session.Id, tool);
	}

	/// <summary>Moves a tool from its dialog to a window of its own. The dialog stays when no window could open.</summary>
	public async Task PopOutAsync(ISessionHandle session, SessionToolDescriptor tool)
	{
		if (await OpenWindowAsync(session, tool))
		{
			CloseDialog(session.Id);
		}
	}

	/// <summary>
	/// Opens the tool's window without a dialog to fall back to, for a window coming back at start once its session
	/// has connected again. False when the tool is not there or the window would not open.
	/// </summary>
	public Task<bool> OpenWindowAsync(ISessionHandle session, SessionToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(tool);
		return Available(session).Contains(tool)
			? _windows.OpenAsync(Target(session, tool), WindowTitle(tool, session), tool.Size)
			: Task.FromResult(false);
	}

	/// <summary>
	/// "Back to the main window": the tool leaves its window for the dialog over its session's tab, which comes to the
	/// front in the window that opened it. The user closed that window, so it does not come back at the next start.
	/// </summary>
	public void ReturnToDialog(ISessionHandle session, SessionToolDescriptor tool)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(tool);
		ShowDialog(session.Id, tool);

		// Before the close: the owner only comes forward while it still has a tool window open.
		_tabs.Show(session.Id);
		_windows.Close(Target(session, tool), byUser: true);
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
		_windows.Changed -= OnWindowsChanged;
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
			_windows.CloseSession(id);
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

		// Tools show hostnames, containers and files, none of which may stay on screen once the vault locks. Their
		// windows go through ShellToolWindows.
		lock (_gate)
		{
			_dialogs.Clear();
		}

		Changed?.Invoke();
	}

	private void OnWindowsChanged() => Changed?.Invoke();
}
