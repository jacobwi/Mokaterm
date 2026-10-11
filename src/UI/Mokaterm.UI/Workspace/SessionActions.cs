using System.Globalization;
using Microsoft.Extensions.Logging;
using Moka.Red.ContextMenu;
using Moka.Red.Core.Icons;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Interaction;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Workspace;

/// <summary>Opening, closing and reconnecting sessions, shared by tabs, menus, commands and shortcuts.</summary>
internal sealed class SessionActions
{
	private readonly ISessionManager _manager;
	private readonly SessionWorkspace _workspace;
	private readonly SessionRestore _restore;
	private readonly IProtocolRegistry _protocols;
	private readonly IUserInteraction _interaction;
	private readonly ISettingsService _settings;
	private readonly ShellState _shell;
	private readonly ILogger<SessionActions> _logger;

	public SessionActions(
		ISessionManager manager,
		SessionWorkspace workspace,
		SessionRestore restore,
		IProtocolRegistry protocols,
		IUserInteraction interaction,
		ISettingsService settings,
		ShellState shell,
		ILogger<SessionActions> logger)
	{
		_manager = manager;
		_workspace = workspace;
		_restore = restore;
		_protocols = protocols;
		_interaction = interaction;
		_settings = settings;
		_shell = shell;
		_logger = logger;
	}

	/// <summary>
	/// Opens a saved login in a new tab. With <paramref name="reuseExisting"/> an open tab for the same login and protocol
	/// is brought forward instead, and reconnected when it dropped.
	/// </summary>
	public async Task OpenAsync(Guid connectionId, string? protocolId = null, bool reuseExisting = false)
	{
		if (reuseExisting && FindOpen(connectionId, protocolId) is { } open)
		{
			Show(open.Id);
			if (SessionStateDisplay.CanReconnect(open.State))
			{
				await ReconnectAsync(open.Id);
			}

			return;
		}

		try
		{
			SessionOpenOptions? options = protocolId is null ? null : new SessionOpenOptions { ProtocolId = protocolId };
			ISessionHandle session = await _manager.OpenAsync(connectionId, options);
			Show(session.Id);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "Opening connection {ConnectionId} failed.", connectionId);
			string message = ex is KeyNotFoundException ? "This login no longer exists." : ex.Message;
			_interaction.Notify(NoticeSeverity.Error, message, "Could not open the session");
		}
	}

	/// <summary>
	/// Opens a connection that is not saved, such as a quick-connect target. Returns its session, or null when it could
	/// not be opened, which the user has been told about.
	/// </summary>
	public ISessionHandle? OpenTransient(HostProfile host, ConnectionProfile connection, string? protocolId = null)
	{
		try
		{
			SessionOpenOptions? options = protocolId is null ? null : new SessionOpenOptions { ProtocolId = protocolId };
			ISessionHandle session = _manager.OpenTransient(host, connection, options);
			Show(session.Id);
			return session;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "Opening a quick-connect session failed.");
			_interaction.Notify(NoticeSeverity.Error, ex.Message, "Could not open the session");
			return null;
		}
	}

	public async Task ReconnectAsync(Guid sessionId)
	{
		if (_workspace.Find(sessionId) is not { } session || !SessionStateDisplay.CanReconnect(session.State))
		{
			return;
		}

		try
		{
			await _manager.ReconnectAsync(sessionId);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "Reconnecting session {SessionId} failed.", sessionId);
			_interaction.Notify(NoticeSeverity.Error, ex.Message, "Could not reconnect");
		}
	}

	/// <summary>
	/// Hands the open quick-connect tab over to the login it was just saved as. Returns true when the tab is now that
	/// login's, so the caller knows there is nothing left to open.
	/// </summary>
	public async Task<bool> AdoptAsync(Guid sessionId, Guid connectionId)
	{
		if (_workspace.Find(sessionId) is not { IsTransient: true })
		{
			return false;
		}

		try
		{
			await _manager.AdoptAsync(sessionId, connectionId);
			return true;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The login is saved either way; the tab simply stays the throwaway one it was.
			_logger.LogWarning(ex, "Adopting session {SessionId} into connection {ConnectionId} failed.", sessionId, connectionId);
			return false;
		}
	}

	/// <summary>Stops a connect in progress, a live connection or a pending automatic reconnect, keeping the tab.</summary>
	public async Task DisconnectAsync(Guid sessionId)
	{
		try
		{
			await _manager.DisconnectAsync(sessionId);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "Disconnecting session {SessionId} failed.", sessionId);
		}
	}

	/// <summary>Opens another session to the same login with the same protocol.</summary>
	public async Task DuplicateAsync(Guid sessionId)
	{
		if (_workspace.Find(sessionId) is not { } session)
		{
			return;
		}

		string? variant = string.Equals(session.Protocol.Id, session.Connection.ProtocolId, StringComparison.OrdinalIgnoreCase)
			? null
			: session.Protocol.Id;

		if (session.IsTransient)
		{
			OpenTransient(session.Host, session.Connection with { Id = Guid.NewGuid() }, variant);
		}
		else
		{
			await OpenAsync(session.Connection.Id, variant);
		}
	}

	/// <summary>The file browser variant for a terminal session, such as SFTP for SSH. Null when there is none.</summary>
	public ProtocolDescriptor? FindFileBrowserVariant(ISessionHandle session) => _protocols.FindFileBrowserVariant(session.Protocol);

	/// <summary>Opens the session's login with its file browser variant, reusing an open file browser tab.</summary>
	public async Task OpenFileBrowserAsync(Guid sessionId)
	{
		if (_workspace.Find(sessionId) is not { } session || FindFileBrowserVariant(session) is not { } variant)
		{
			return;
		}

		if (session.IsTransient)
		{
			OpenTransient(session.Host, session.Connection with { Id = Guid.NewGuid() }, variant.Id);
		}
		else
		{
			await OpenAsync(session.Connection.Id, variant.Id, reuseExisting: true);
		}
	}

	/// <summary>Closes sessions, asking first when any is connected and the settings say so.</summary>
	public async Task CloseAsync(IReadOnlyCollection<Guid> sessionIds)
	{
		List<ISessionHandle> sessions = [.. sessionIds.Select(_workspace.Find).OfType<ISessionHandle>()];
		if (sessions.Count == 0)
		{
			return;
		}

		int connected = sessions.Count(session => session.State == SessionState.Connected);
		if (connected > 0 && _settings.Get<GeneralSettings>().ConfirmCloseConnectedSessions)
		{
			ConfirmPrompt prompt = sessions.Count == 1
				? new ConfirmPrompt
				{
					Title = "Close session",
					Message = $"{sessions[0].Title} is still connected. Closing the tab disconnects it.",
					ConfirmText = "Close",
					Destructive = true,
					RememberText = PromptOptOut.Label,
				}
				: new ConfirmPrompt
				{
					Title = "Close sessions",
					Message = string.Create(CultureInfo.CurrentCulture, $"{connected} of {sessions.Count} sessions are still connected. Closing the tabs disconnects them."),
					ConfirmText = "Close all",
					Destructive = true,
					RememberText = PromptOptOut.Label,
				};

			ConfirmResult answer = await _interaction.ConfirmAsync(prompt);
			if (!answer.Confirmed)
			{
				return;
			}

			if (answer.Remember)
			{
				await PromptOptOut.ApplyAsync<GeneralSettings>(
					_settings,
					_interaction,
					_logger,
					settings => settings with { ConfirmCloseConnectedSessions = false },
					"Connected sessions now close without asking. Settings, General turns it back on.");
			}
		}

		foreach (ISessionHandle session in sessions)
		{
			try
			{
				// Closing a tab here is the user's doing, so the next start should not offer it again.
				_restore.Forget(session.Id);
				await _manager.CloseAsync(session.Id);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.LogWarning(ex, "Closing session {SessionId} failed.", session.Id);
			}
		}
	}

	public Task CloseOthersAsync(Guid keepSessionId) =>
		CloseAsync([.. _workspace.Tabs.Where(session => session.Id != keepSessionId).Select(session => session.Id)]);

	public Task CloseAllAsync() => CloseAsync([.. _workspace.Tabs.Select(session => session.Id)]);

	/// <summary>Opens the connection editor prefilled from a quick-connect session.</summary>
	public void SaveConnection(ISessionHandle session)
	{
		if (session.IsTransient)
		{
			_shell.OpenEditor(new ConnectionEditorRequest { TransientSession = session });
		}
	}

	/// <summary>The per-session actions shared by the tab menu and the terminal menu.</summary>
	public List<MokaContextMenuItem> BuildSessionItems(ISessionHandle session)
	{
		List<MokaContextMenuItem> items =
		[
			new()
			{
				Text = "Reconnect",
				Icon = MokatermIcons.Reconnect,
				Shortcut = ShellShortcuts.LabelFor(ShellCommandIds.Reconnect),
				Disabled = !SessionStateDisplay.CanReconnect(session.State),
				OnClick = () => ReconnectAsync(session.Id),
			},
			new()
			{
				Text = "Disconnect",
				Icon = MokatermIcons.Disconnect,
				Disabled = session.State is not (SessionState.Connecting or SessionState.Connected),
				OnClick = () => DisconnectAsync(session.Id),
			},
			new()
			{
				Text = "Duplicate",
				Icon = MokaIcons.Content.Copy,
				Shortcut = ShellShortcuts.LabelFor(ShellCommandIds.Duplicate),
				OnClick = () => DuplicateAsync(session.Id),
			},
		];

		if (FindFileBrowserVariant(session) is { } variant)
		{
			items.Add(new MokaContextMenuItem
			{
				Text = $"Open {variant.DisplayName} browser",
				Icon = MokatermIcons.FolderOpen,
				OnClick = () => OpenFileBrowserAsync(session.Id),
			});
		}

		if (session.IsTransient)
		{
			items.Add(new MokaContextMenuItem
			{
				Text = "Save connection",
				Icon = MokaIcons.Action.Save,
				OnClickSync = () => SaveConnection(session),
			});
		}

		return items;
	}

	public IReadOnlyList<MokaContextMenuItem> BuildTabMenu(ISessionHandle session)
	{
		List<MokaContextMenuItem> items = BuildSessionItems(session);
		int tabCount = _workspace.Tabs.Count;
		items.Add(new MokaContextMenuItem
		{
			Text = "Move to pane",
			Icon = MokatermIcons.SplitView,
			Children = BuildPaneItems(session),
		});
		items.Add(new MokaContextMenuItem
		{
			Text = "Close",
			Icon = MokaIcons.Navigation.Close,
			Shortcut = ShellShortcuts.LabelFor(ShellCommandIds.CloseTab),
			DividerBefore = true,
			OnClick = () => CloseAsync([session.Id]),
		});
		items.Add(new MokaContextMenuItem
		{
			Text = "Close others",
			Disabled = tabCount < 2,
			OnClick = () => CloseOthersAsync(session.Id),
		});
		items.Add(new MokaContextMenuItem
		{
			Text = "Close all",
			OnClick = CloseAllAsync,
		});
		return items;
	}

	/// <summary>
	/// The tab menu's "Move to pane" entries: each pane by number, a new pane on each side of this one, and every tab
	/// back in one pane.
	/// </summary>
	public IReadOnlyList<MokaContextMenuItem> BuildPaneItems(ISessionHandle session)
	{
		IReadOnlyList<int> panes = _workspace.Panes;
		int here = _workspace.PaneOf(session.Id);

		// A new pane needs a tab to stay behind in this one, and room in the workspace.
		bool cannotSplit = _workspace.TabsIn(here).Count < 2 || panes.Count >= SessionWorkspace.MaxPanes;
		List<MokaContextMenuItem> items = [];
		for (int position = 0; position < panes.Count; position++)
		{
			int target = position;
			bool current = panes[position] == here;
			items.Add(new MokaContextMenuItem
			{
				Text = string.Create(CultureInfo.CurrentCulture, $"Pane {position + 1}"),
				Checked = current,
				Disabled = current,
				Shortcut = ShellShortcuts.LabelFor(ShellCommandIds.MoveToPane[position]),
				OnClickSync = () => _workspace.MoveToPane(session.Id, target),
			});
		}

		items.Add(NewPaneItem(session, "New pane on the right", MokatermIcons.SplitView, PaneDirection.Right, ShellCommandIds.NewPaneRight, cannotSplit, true));
		items.Add(NewPaneItem(session, "New pane below", MokatermIcons.SplitStacked, PaneDirection.Down, ShellCommandIds.NewPaneDown, cannotSplit, false));
		items.Add(NewPaneItem(session, "New pane on the left", MokatermIcons.SplitView, PaneDirection.Left, ShellCommandIds.NewPaneLeft, cannotSplit, false));
		items.Add(NewPaneItem(session, "New pane above", MokatermIcons.SplitStacked, PaneDirection.Up, ShellCommandIds.NewPaneUp, cannotSplit, false));
		items.Add(new MokaContextMenuItem
		{
			Text = "Every tab in one pane",
			Icon = MokatermIcons.SinglePane,
			Shortcut = ShellShortcuts.LabelFor(ShellCommandIds.SinglePane),
			Disabled = panes.Count < 2,
			DividerBefore = true,
			OnClickSync = () => _workspace.Arrange(PaneArrangement.Single),
		});
		return items;
	}

	private MokaContextMenuItem NewPaneItem(
		ISessionHandle session,
		string text,
		MokaIconDefinition icon,
		PaneDirection direction,
		string commandId,
		bool disabled,
		bool dividerBefore) => new()
	{
		Text = text,
		Icon = icon,
		Shortcut = ShellShortcuts.LabelFor(commandId),
		Disabled = disabled,
		DividerBefore = dividerBefore,
		OnClickSync = () => _workspace.MoveToNewPane(session.Id, direction),
	};

	private void Show(Guid sessionId)
	{
		_shell.CloseSettings();
		_workspace.Activate(sessionId);
	}

	private ISessionHandle? FindOpen(Guid connectionId, string? protocolId) =>
		_workspace.Tabs.FirstOrDefault(session =>
			!session.IsTransient
			&& session.Connection.Id == connectionId
			&& string.Equals(session.Protocol.Id, protocolId ?? session.Connection.ProtocolId, StringComparison.OrdinalIgnoreCase));
}
