using Moka.Red.Core.Icons;
using Moka.Red.Feedback.Cheatsheet;
using Moka.Red.Feedback.CommandPalette;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Commands;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Shell;

/// <summary>
/// Registers the shell's commands in the Moka command palette while the shell is mounted, including one "Connect"
/// command per saved login, and runs the same commands for keyboard shortcuts.
/// </summary>
internal sealed class ShellCommandRegistry : IDisposable
{
	private const string ConnectCommandPrefix = "mokaterm.connect.";

	private readonly IMokaCommandPaletteService _palette;
	private readonly ShellState _shell;
	private readonly SessionWorkspace _workspace;
	private readonly SessionActions _sessions;
	private readonly ConnectionActions _connections;
	private readonly CommandSnippetActions _commands;
	private readonly InputBroadcast _broadcast;
	private readonly CatalogState _catalog;
	private readonly IProtocolRegistry _protocols;
	private readonly IUiContributions _contributions;
	private readonly IUserInteraction _interaction;
	private readonly IVault _vault;
	private readonly Lock _gate = new();
	private readonly IReadOnlyList<CommandDefinition> _definitions;
	private readonly Dictionary<string, Func<Task>> _handlers = new(StringComparer.Ordinal);
	private readonly List<string> _connectCommandIds = [];
	private IReadOnlyList<ShellShortcutBinding>? _cheatsheetFor;
	private IReadOnlyList<MokaCheatsheetGroup> _cheatsheet = [];
	private bool _registered;

	public ShellCommandRegistry(
		IMokaCommandPaletteService palette,
		ShellState shell,
		SessionWorkspace workspace,
		SessionActions sessions,
		ConnectionActions connections,
		CommandSnippetActions commands,
		InputBroadcast broadcast,
		CatalogState catalog,
		IProtocolRegistry protocols,
		IUiContributions contributions,
		IUserInteraction interaction,
		IVault vault)
	{
		_palette = palette;
		_shell = shell;
		_workspace = workspace;
		_sessions = sessions;
		_connections = connections;
		_commands = commands;
		_broadcast = broadcast;
		_catalog = catalog;
		_protocols = protocols;
		_contributions = contributions;
		_interaction = interaction;
		_vault = vault;
		_definitions = BuildDefinitions();
		foreach (CommandDefinition definition in _definitions)
		{
			_handlers[definition.Id] = definition.Execute;
		}

		// Shortcut only: listing "open the command palette" inside the palette would be pointless.
		_handlers[ShellCommandIds.CommandPalette] = Sync(_palette.Open);
	}

	/// <summary>Rebuilt whenever the gestures change, so the sheet shows what is in force.</summary>
	public IReadOnlyList<MokaCheatsheetGroup> CheatsheetGroups
	{
		get
		{
			IReadOnlyList<ShellShortcutBinding> bindings = ShellShortcuts.Bindings;
			if (!ReferenceEquals(_cheatsheetFor, bindings))
			{
				_cheatsheetFor = bindings;
				_cheatsheet = BuildCheatsheet();
			}

			return _cheatsheet;
		}
	}

	/// <summary>Every command that can carry a shortcut, for the keyboard settings page.</summary>
	public IReadOnlyList<ShellCommandInfo> Commands =>
	[
		new(ShellCommandIds.CommandPalette, "Command palette", "View"),
		.. _definitions.Select(definition => new ShellCommandInfo(definition.Id, definition.Title, definition.Group)),
	];

	/// <summary>Puts changed gestures on the palette's entries, which carry their shortcut as text.</summary>
	public void RefreshShortcutLabels()
	{
		lock (_gate)
		{
			if (!_registered)
			{
				return;
			}
		}

		_palette.RegisterMany(_definitions.Select(ToCommand));
	}

	public void Register()
	{
		lock (_gate)
		{
			if (_registered)
			{
				return;
			}

			_registered = true;
		}

		_palette.RegisterMany(_definitions.Select(ToCommand));
		_catalog.Changed += OnCatalogChanged;
		RefreshConnectCommands();
	}

	public void Unregister()
	{
		lock (_gate)
		{
			if (!_registered)
			{
				return;
			}

			_registered = false;
		}

		_catalog.Changed -= OnCatalogChanged;
		foreach (CommandDefinition definition in _definitions)
		{
			_palette.Unregister(definition.Id);
		}

		RemoveConnectCommands();
	}

	/// <summary>Runs a command by id, for keyboard shortcuts. Does nothing while the shell is not mounted.</summary>
	public Task ExecuteAsync(string commandId)
	{
		lock (_gate)
		{
			if (!_registered)
			{
				return Task.CompletedTask;
			}
		}

		return _handlers.TryGetValue(commandId, out Func<Task>? handler) ? handler() : Task.CompletedTask;
	}

	public void Dispose() => Unregister();

	private static MokaCommand ToCommand(CommandDefinition definition) => new()
	{
		Id = definition.Id,
		Title = definition.Title,
		Description = definition.Description,
		Group = definition.Group,
		Icon = definition.Icon,
		Keywords = definition.Keywords,
		Shortcut = ShellShortcuts.LabelFor(definition.Id),
		OnExecute = definition.Execute,
	};

	private List<CommandDefinition> BuildDefinitions() =>
	[
		new(ShellCommandIds.NewLogin, "New login", "Connections", MokatermIcons.User, Sync(() => _connections.NewLogin()), "Add a login to a saved host", "connection user ssh ftp"),
		new(ShellCommandIds.NewHost, "New host", "Connections", MokatermIcons.Server, Sync(() => _connections.NewHost()), "Save a machine to connect to", "server address"),
		new(ShellCommandIds.NewFolder, "New folder", "Connections", MokatermIcons.FolderPlus, Sync(() => _connections.NewFolder()), "Group hosts in the connections tree", "group"),
		new(ShellCommandIds.QuickConnect, "Quick connect", "Connections", MokatermIcons.QuickConnect, Sync(_shell.FocusQuickConnect), "Connect to user@host without saving it", "open ssh"),
		new(ShellCommandIds.ImportConnections, "Import saved hosts", "Connections", MokatermIcons.FileUpload, Sync(() => _connections.ImportConnections()), "Bring hosts over from OpenSSH, PuTTY or WinSCP", "migrate putty winscp openssh config sessions"),
		new(ShellCommandIds.ExportConnections, "Export saved hosts", "Connections", MokatermIcons.FileDownload, Sync(() => _connections.ExportConnections()), "Write every host and login to a file, without any password", "backup migrate move machine export"),
		new(ShellCommandIds.ToggleConnections, "Toggle connections panel", "View", MokatermIcons.PanelLeft, Sync(_shell.ToggleConnectionsPanel), null, "sidebar explorer tree"),
		new(ShellCommandIds.ToggleTransfers, "Toggle transfers panel", "View", MokatermIcons.Transfers, Sync(_shell.ToggleTransfersPanel), null, "upload download queue"),
		new(ShellCommandIds.Settings, "Settings", "View", MokaIcons.Action.Settings, Sync(() => _shell.OpenSettings()), null, "preferences options"),
		new(ShellCommandIds.Shortcuts, "Keyboard shortcuts", "View", MokatermIcons.Keyboard, Sync(() => _shell.SetShortcutsOpen(true)), null, "keys cheatsheet"),
		new(ShellCommandIds.NextTab, "Next tab", "Sessions", MokaIcons.Navigation.ChevronRight, Sync(() => SwitchTab(1)), null, "session"),
		new(ShellCommandIds.PreviousTab, "Previous tab", "Sessions", MokaIcons.Navigation.ChevronLeft, Sync(() => SwitchTab(-1)), null, "session"),
		new(ShellCommandIds.MoveToOtherPane, "Move tab to the other pane", "Sessions", MokatermIcons.SplitView, MoveToOtherPane, "Split the workspace, or move this tab back", "split side by side pane"),
		new(ShellCommandIds.CloseTab, "Close tab", "Sessions", MokaIcons.Navigation.Close, () => WithActiveSession(session => _sessions.CloseAsync([session.Id])), "Close the active session", "disconnect"),
		new(ShellCommandIds.CloseAllTabs, "Close all tabs", "Sessions", MokaIcons.Navigation.Close, _sessions.CloseAllAsync, "Close every open session", "disconnect"),
		new(ShellCommandIds.Reconnect, "Reconnect", "Sessions", MokatermIcons.Reconnect, () => WithActiveSession(session => _sessions.ReconnectAsync(session.Id)), "Reconnect the active session", "retry"),
		new(ShellCommandIds.Disconnect, "Disconnect", "Sessions", MokatermIcons.Disconnect, () => WithActiveSession(session => _sessions.DisconnectAsync(session.Id)), "Disconnect the active session but keep its tab", "stop cancel"),
		new(ShellCommandIds.Duplicate, "Duplicate session", "Sessions", MokaIcons.Content.Copy, () => WithActiveSession(session => _sessions.DuplicateAsync(session.Id)), "Open the active login again in a new tab", "clone"),
		new(ShellCommandIds.OpenFileBrowser, "Open file browser for this session", "Sessions", MokatermIcons.FolderOpen, OpenFileBrowserAsync, "Open SFTP for the active SSH session", "sftp files"),
		new(ShellCommandIds.SavedCommands, "Saved commands", "Sessions", MokatermIcons.Command, OpenSavedCommands, "Pick a saved command and type or run it here", "snippets notes history run"),
		new(ShellCommandIds.BroadcastInput, "Type into several sessions", "Sessions", MokatermIcons.Broadcast, ToggleBroadcast, "Turn this on in each session that should get what you type", "broadcast sync input multi"),
		new(ShellCommandIds.LockVault, "Lock vault", "Security", MokatermIcons.Lock, Sync(() => _vault.Lock(LockReason.User)), "Hide everything until the master password is entered", "security"),
	];

	private List<MokaCheatsheetGroup> BuildCheatsheet()
	{
		List<MokaCheatsheetGroup> groups = [];
		foreach (IGrouping<string, CommandDefinition> group in _definitions.GroupBy(definition => definition.Group))
		{
			List<MokaCheatsheetItem> items =
			[
				.. group
					.Select(definition => (definition.Title, Keys: ShellShortcuts.KeysFor(definition.Id)))
					.Where(entry => entry.Keys.Count > 0)
					.Select(entry => new MokaCheatsheetItem(entry.Title, entry.Keys)),
			];

			if (group.Key == "View")
			{
				// Moka's own palette shortcut; it does not reach the palette while a terminal has focus.
				items.Insert(0, new MokaCheatsheetItem("Command palette (outside terminals)", ["Ctrl", "K"]));
				items.Insert(1, new MokaCheatsheetItem("Command palette", ShellShortcuts.KeysFor(ShellCommandIds.CommandPalette)));
			}

			if (items.Count > 0)
			{
				groups.Add(new MokaCheatsheetGroup(group.Key, items));
			}
		}

		return groups;
	}

	private void SwitchTab(int offset)
	{
		_shell.CloseSettings();
		_workspace.ActivateRelative(offset);
	}

	private Task WithActiveSession(Func<ISessionHandle, Task> action)
	{
		if (_workspace.Active is { } session)
		{
			return action(session);
		}

		_interaction.Notify(NoticeSeverity.Info, "Open a session first.");
		return Task.CompletedTask;
	}

	private Task OpenSavedCommands() => WithActiveSession(session =>
	{
		_commands.OpenPicker(session);
		return Task.CompletedTask;
	});

	private Task MoveToOtherPane() => WithActiveSession(session =>
	{
		_workspace.MoveToOtherPane(session.Id);
		return Task.CompletedTask;
	});

	private Task ToggleBroadcast() => WithActiveSession(session =>
	{
		if (_broadcast.Toggle(session.Id) && _broadcast.Count == 1)
		{
			_interaction.Notify(NoticeSeverity.Info, "Turn this on in another session too, then typing goes to both.");
		}

		return Task.CompletedTask;
	});

	private Task OpenFileBrowserAsync() => WithActiveSession(session =>
	{
		if (_sessions.FindFileBrowserVariant(session) is null)
		{
			_interaction.Notify(NoticeSeverity.Info, "The active session has no file browser to open.");
			return Task.CompletedTask;
		}

		return _sessions.OpenFileBrowserAsync(session.Id);
	});

	private void OnCatalogChanged() => RefreshConnectCommands();

	private void RefreshConnectCommands()
	{
		RemoveConnectCommands();

		lock (_gate)
		{
			if (!_registered)
			{
				return;
			}
		}

		ConnectionCatalog catalog = _catalog.Catalog;
		if (!_catalog.IsLoaded)
		{
			_ = _catalog.EnsureLoadedAsync();
			return;
		}

		List<MokaCommand> commands = [];
		foreach (ConnectionProfile login in catalog.Connections)
		{
			if (catalog.FindHost(login.HostId) is not { } host)
			{
				continue;
			}

			Guid loginId = login.Id;
			commands.Add(new MokaCommand
			{
				Id = ConnectCommandPrefix + loginId.ToString("N"),
				Title = $"Connect {login.GetTitle(host)}",
				Description = $"{_protocols.DisplayName(login.ProtocolId)} on {host.DisplayName}",
				Group = "Connect",
				Icon = _contributions.GetProtocolIcon(login.ProtocolId),
				Keywords = string.Join(' ', host.Tags.Prepend(login.ProtocolId).Prepend(host.Address).Prepend(host.Name)),
				OnExecute = () => _sessions.OpenAsync(loginId, reuseExisting: true),
			});
		}

		lock (_gate)
		{
			_connectCommandIds.AddRange(commands.Select(command => command.Id));
		}

		_palette.RegisterMany(commands);
	}

	private void RemoveConnectCommands()
	{
		List<string> ids;
		lock (_gate)
		{
			ids = [.. _connectCommandIds];
			_connectCommandIds.Clear();
		}

		foreach (string id in ids)
		{
			_palette.Unregister(id);
		}
	}

	private static Func<Task> Sync(Action action) => () =>
	{
		action();
		return Task.CompletedTask;
	};

	private sealed record CommandDefinition(
		string Id,
		string Title,
		string Group,
		MokaIconDefinition Icon,
		Func<Task> Execute,
		string? Description,
		string? Keywords);
}
