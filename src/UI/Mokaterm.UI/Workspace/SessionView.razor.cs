using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Moka.Red.ContextMenu;
using Moka.Red.Core.Enums;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Commands;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Interaction;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Terminal;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// One session tab: a thin toolbar over a body chosen by the protocol's capabilities, plus connecting, failure and
/// disconnect states. The body mounts once the session first connects and then stays, so reconnects keep the terminal.
/// </summary>
public sealed partial class SessionView : ComponentBase, IDisposable
{
	private ISessionHandle? _session;
	private ConnectionProfile? _connection;
	private SessionViewState _viewState = new();
	private TerminalSettings _terminalSettings = new();
	private FileBrowserPlacement _placement;
	private TerminalView? _terminal;
	private CommandsPanel? _commands;
	private bool _focusCommands;
	private SessionToolDescriptor? _openTool;
	private BroadcastTerminalStream? _broadcastStream;

	private enum SessionLayout
	{
		None,
		Split,
		Terminal,
		Files,
		Module,
	}

	[Parameter, EditorRequired]
	public ISessionHandle Session { get; set; } = default!;

	/// <summary>True while this session's tab is the visible one.</summary>
	[Parameter]
	public bool IsActive { get; set; }

	[Inject]
	private SessionWorkspace Workspace { get; set; } = default!;

	[Inject]
	private SessionActions Actions { get; set; } = default!;

	[Inject]
	private ConnectionActions Connections { get; set; } = default!;

	[Inject]
	private CommandSnippetActions Commands { get; set; } = default!;

	[Inject]
	private ITerminalThemeCatalog ThemeCatalog { get; set; } = default!;

	[Inject]
	private IMokaContextMenuService Menu { get; set; } = default!;

	[Inject]
	private ShellState Shell { get; set; } = default!;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private IUiContributions Contributions { get; set; } = default!;

	[Inject]
	private UiStateStore UiState { get; set; } = default!;

	[Inject]
	private InputBroadcast Broadcast { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private ISessionLogRecorder SessionLogs { get; set; } = default!;

	[Inject]
	private ILogger<SessionView> Logger { get; set; } = default!;

	/// <summary>The stream the terminal view writes to: this session's, with the broadcast tap in front of it.</summary>
	private ITerminalStream? TerminalStream => _broadcastStream;

	private bool TypesWithOthers => Broadcast.IsMember(Session.Id);

	private string BroadcastTitle => TypesWithOthers
		? Broadcast.Count > 1
			? string.Create(CultureInfo.CurrentCulture, $"Typing goes to {Broadcast.Count} sessions")
			: "Waiting for another session to type with"
		: "Type into several sessions at once";

	private Type? ModuleViewType =>
		Contributions.FindProtocol(Session.Protocol.Id)?.SessionView
		?? (Session.Protocol.VariantOf is { } parent ? Contributions.FindProtocol(parent)?.SessionView : null);

	private SessionLayout Layout
	{
		get
		{
			if (ModuleViewType is not null)
			{
				return SessionLayout.Module;
			}

			bool terminal = Session.Protocol.Has(ProtocolCapabilities.Terminal) && Session.Terminal is not null;
			bool files = Session.Protocol.Has(ProtocolCapabilities.FileSystem);
			return (terminal, files) switch
			{
				(true, true) => SessionLayout.Split,
				(true, false) => SessionLayout.Terminal,
				(false, true) => SessionLayout.Files,
				_ => SessionLayout.None,
			};
		}
	}

	private bool HasTerminalView => Session.ConnectedAt is not null && Layout is SessionLayout.Split or SessionLayout.Terminal;

	private bool ShowBanner =>
		Session.State == SessionState.Disconnected || (Session.State == SessionState.Failed && Session.ConnectedAt is not null);

	private string BannerText => Session.State == SessionState.Failed
		? $"{SessionStateDisplay.FailureTitle(Session.Failure)}: {Session.StatusMessage ?? "the connection failed"}"
		: Session.StatusMessage ?? "Disconnected";

	private string Endpoint => EndpointFormat.Format(Session);

	/// <summary>
	/// Marks the split for the stylesheet: whether the file browser is there, which side it is on, whether it is
	/// showing, and whether a second panel shares the row. The width rules for a narrow session need all four, and the
	/// dock layout gives none of them away.
	/// </summary>
	private string SplitClass =>
		"mt-fill mt-split"
		+ (Layout == SessionLayout.Split ? " mt-split-files" : "")
		+ (_placement == FileBrowserPlacement.Right ? " mt-split-right" : "")
		+ (_viewState.FilesCollapsed ? " mt-split-collapsed" : "")
		+ (_viewState.CommandsOpen && HasTerminalView ? " mt-split-commands" : "");

	// The narrow sizes where this width is capped are in the stylesheet: a track the splitter can still drag has to
	// stay a plain pixel value, because the dock layout's drag script reads the template apart on spaces.
	private MokaDockPosition CommandsDock =>
		_placement == FileBrowserPlacement.Right ? MokaDockPosition.Left : MokaDockPosition.Right;

	private string CommandsPanelSize =>
		string.Create(CultureInfo.InvariantCulture, $"{Math.Round(UiState.Current.CommandsPanelWidth ?? 280)}px");

	private string FilesPanelSize =>
		string.Create(CultureInfo.InvariantCulture, $"{Math.Round(UiState.Current.FilesPanelWidth ?? 340)}px");

	private Dictionary<string, object> ModuleParameters => new()
	{
		[nameof(SessionViewBase.Session)] = Session,
		[nameof(SessionViewBase.IsActive)] = IsActive,
	};

	/// <summary>Toolbar views modules registered for this protocol, such as the SSH tunnels panel.</summary>
	private IReadOnlyList<SessionToolDescriptor> Tools
	{
		get
		{
			IReadOnlyList<SessionToolDescriptor> tools = Contributions.FindSessionTools(Session.Protocol.Id);
			return tools.Count == 0 || Session.State == SessionState.Connected
				? tools
				: [.. tools.Where(tool => tool.ShowWhileDisconnected)];
		}
	}

	/// <summary>The tool whose dialog is showing. A tool that went away with the connection closes itself.</summary>
	private SessionToolDescriptor? OpenTool => _openTool is { } tool && Tools.Contains(tool) ? tool : null;

	private string ActiveThemeId => ThemeCatalog.Get(_terminalSettings.ThemeId).Id;

	private IReadOnlyList<MokaContextMenuItem> TerminalMenuItems
	{
		get
		{
			List<MokaContextMenuItem> items = Actions.BuildSessionItems(Session);
			items.Insert(0, new MokaContextMenuItem
			{
				Text = "Saved commands",
				Icon = MokatermIcons.Command,
				Shortcut = ShellShortcuts.LabelFor(ShellCommandIds.SavedCommands),
				OnClickSync = ToggleCommands,
			});
			items.Add(new MokaContextMenuItem
			{
				Text = "Theme",
				Icon = MokatermIcons.Palette,
				DividerBefore = true,
				Children = BuildThemeMenu(),
			});
			if (Layout == SessionLayout.Split)
			{
				items.Add(new MokaContextMenuItem
				{
					Text = _viewState.FilesCollapsed ? "Show file browser" : "Hide file browser",
					Icon = MokatermIcons.SplitView,
					OnClickSync = ToggleFiles,
				});
			}

			items.Add(new MokaContextMenuItem
			{
				Text = SessionLogs.StatusFor(Session.Id).IsOn ? "Stop recording to a file" : "Record to a file",
				Icon = MokatermIcons.Record,
				OnClickSync = ToggleLog,
			});
			items.Add(new MokaContextMenuItem
			{
				Text = "Disconnect and close",
				Icon = MokatermIcons.Power,
				Shortcut = ShellShortcuts.LabelFor(ShellCommandIds.CloseTab),
				DividerBefore = true,
				OnClick = CloseAsync,
			});
			return items;
		}
	}

	public void Dispose()
	{
		Settings.Changed -= OnSettingsChanged;
		Commands.TerminalFocusRequested -= OnTerminalFocusRequested;
		Commands.CommandsRequested -= OnCommandsRequested;
		Broadcast.Changed -= OnBroadcastChanged;
		SessionLogs.Changed -= OnSessionLogChanged;
	}

	protected override void OnInitialized()
	{
		_placement = Settings.Get<FileTransferSettings>().Placement;
		Settings.Changed += OnSettingsChanged;
		Commands.TerminalFocusRequested += OnTerminalFocusRequested;
		Commands.CommandsRequested += OnCommandsRequested;
		Broadcast.Changed += OnBroadcastChanged;
		SessionLogs.Changed += OnSessionLogChanged;
	}

	protected override void OnParametersSet()
	{
		// The handle keeps one stream across reconnects, so the tap in front of it is built once per session.
		if (Session.Terminal is { } terminal)
		{
			if (_broadcastStream is null || !_broadcastStream.Wraps(terminal))
			{
				_broadcastStream = new BroadcastTerminalStream(terminal, Session.Id, Broadcast);
			}
		}
		else
		{
			_broadcastStream = null;
		}

		if (ReferenceEquals(_session, Session))
		{
			// The login can be edited while its session is open, which may change the terminal's theme or font.
			if (!ReferenceEquals(_connection, Session.Connection))
			{
				_connection = Session.Connection;
				ApplyTerminalSettings();
			}

			return;
		}

		_session = Session;
		_connection = Session.Connection;
		_viewState = Workspace.GetViewState(Session.Id, () => new SessionViewState
		{
			FilesCollapsed = !Settings.Get<FileTransferSettings>().OpenWithTerminal,
		});
		ApplyTerminalSettings();
	}

	protected override void OnAfterRender(bool firstRender)
	{
		if (_focusCommands && _commands is { } panel)
		{
			_focusCommands = false;
			panel.Focus();
		}
	}

	private static MokaColor LogColor(SessionLogStatus log) =>
		log.State == SessionLogState.Recording ? MokaColor.Error : MokaColor.Warning;

	private static string LogLabel(SessionLogStatus log) => log.State switch
	{
		SessionLogState.Full => "LOG FULL",
		SessionLogState.Failed => "LOG FAILED",
		_ => "REC",
	};

	private static string LogTitle(SessionLogStatus log) => log.State switch
	{
		SessionLogState.Recording => log.FileName is { } name
			? string.Create(CultureInfo.CurrentCulture, $"Recording to {name} ({DisplayFormat.Bytes(log.Bytes)})")
			: "Recording this session to a file",
		SessionLogState.Full => log.Message ?? "The log file is full",
		SessionLogState.Failed => log.Message ?? "The log file could not be written",
		_ => "Record what this session prints to a file",
	};

	private void ToggleTool(SessionToolDescriptor tool) => _openTool = ReferenceEquals(_openTool, tool) ? null : tool;

	private void OnToolDialogOpenChanged(bool open)
	{
		if (!open)
		{
			_openTool = null;
		}
	}

	private void ToggleBroadcast()
	{
		if (Broadcast.Toggle(Session.Id) && Broadcast.Count == 1)
		{
			// On its own it does nothing, and nothing on screen would say why.
			Interaction.Notify(NoticeSeverity.Info, "Turn this on in another session too, then typing goes to both.");
		}
	}

	private void OnBroadcastChanged() => _ = InvokeAsync(StateHasChanged);

	private void OnSessionLogChanged() => _ = InvokeAsync(StateHasChanged);

	private void ToggleLog() => SetRecording(!SessionLogs.StatusFor(Session.Id).IsOn);

	private void SetRecording(bool record)
	{
		SessionLogs.SetRecording(Session.Id, record);
		if (record)
		{
			// Where the file lands is the one thing the toolbar cannot show, and it is what the user needs next.
			Interaction.Notify(
				NoticeSeverity.Info,
				string.Create(CultureInfo.CurrentCulture, $"Writing what this session prints to {SessionLogs.Folder}."),
				"Recording");
		}

		// Also reached from the terminal's context menu, whose click handler belongs to another component.
		_ = InvokeAsync(StateHasChanged);
	}

	private void ToggleFiles() => SetFilesCollapsed(!_viewState.FilesCollapsed);

	private void SetFilesCollapsed(bool collapsed)
	{
		if (_viewState.FilesCollapsed == collapsed)
		{
			return;
		}

		_viewState.FilesCollapsed = collapsed;

		// Also reached from the terminal's context menu, whose click handler belongs to another component.
		_ = InvokeAsync(StateHasChanged);
	}

	// Global settings, then the theme for the host's environment, then the connection's overrides, then this session's pick.
	/// <summary>The terminal asked for the paste question to stop; the setting behind it lives here.</summary>
	private Task StopConfirmingPasteAsync() => PromptOptOut.ApplyAsync<TerminalSettings>(
		Settings,
		Interaction,
		Logger,
		settings => settings with { ConfirmMultiLinePaste = false },
		"Pastes with line breaks no longer ask. Settings, Terminal turns it back on.");

	private void ApplyTerminalSettings()
	{
		TerminalSettings settings = Settings.Get<TerminalSettings>()
			.ForEnvironment(Session.Host.Environment)
			.WithOverrides(Session.Connection.Terminal);
		_terminalSettings = _viewState.ThemeId is { } themeId ? settings with { ThemeId = themeId } : settings;
	}

	private void ShowThemeMenu(MouseEventArgs args) => Menu.Show(args, BuildThemeMenu());

	private List<MokaContextMenuItem> BuildThemeMenu()
	{
		string activeId = ActiveThemeId;
		List<MokaContextMenuItem> items = [];
		foreach (TerminalTheme theme in ThemeCatalog.Themes)
		{
			string themeId = theme.Id;
			items.Add(new MokaContextMenuItem
			{
				Text = theme.Name,
				Checked = themeId == activeId,
				OnClickSync = () => UseTheme(themeId),
			});
		}

		items.Add(new MokaContextMenuItem
		{
			Text = "Save for this connection",
			Icon = MokatermIcons.Palette,
			DividerBefore = true,
			Disabled = Session.IsTransient,
			OnClick = () => Connections.SetTerminalThemeAsync(Session.Connection, activeId),
		});
		items.Add(new MokaContextMenuItem
		{
			Text = "Use for every terminal",
			OnClick = () => Settings.UpdateAsync<TerminalSettings>(settings => settings with { ThemeId = activeId }),
		});
		if (_viewState.ThemeId is not null)
		{
			items.Add(new MokaContextMenuItem { Text = "Back to the saved theme", OnClickSync = () => UseTheme(null) });
		}

		return items;
	}

	// Also reached from the terminal's context menu, whose click handler belongs to another component.
	private void UseTheme(string? themeId)
	{
		_viewState.ThemeId = themeId;
		ApplyTerminalSettings();
		_ = InvokeAsync(StateHasChanged);
	}

	private void OnFilesPanelResized(double width) => UiState.Update(state => state with { FilesPanelWidth = width });

	private void OnRemoteTitleChanged(string title) => Workspace.SetRemoteTitle(Session.Id, title);

	private Task ReconnectAsync() => Actions.ReconnectAsync(Session.Id);

	private Task DisconnectAsync() => Actions.DisconnectAsync(Session.Id);

	private Task DuplicateAsync() => Actions.DuplicateAsync(Session.Id);

	private Task CloseAsync() => Actions.CloseAsync([Session.Id]);

	private void EditConnection() => Shell.OpenEditor(new ConnectionEditorRequest { ConnectionId = Session.Connection.Id });

	private void ToggleCommands() => SetCommandsOpen(!_viewState.CommandsOpen);

	private void CloseCommands() => SetCommandsOpen(false);

	private void SetCommandsOpen(bool open)
	{
		if (_viewState.CommandsOpen == open)
		{
			// Asked for again while it is already open: put the keyboard back in its search box.
			if (open)
			{
				_focusCommands = true;
				StateHasChanged();
			}

			return;
		}

		_viewState.CommandsOpen = open;
		_focusCommands = open;
		StateHasChanged();
	}

	private void OnCommandsRequested(Guid sessionId)
	{
		if (sessionId == Session.Id)
		{
			_ = InvokeAsync(() => SetCommandsOpen(true));
		}
	}

	private void OnCommandsPanelResized(double width) => UiState.Update(state => state with { CommandsPanelWidth = width });

	private void OnTerminalFocusRequested(Guid sessionId)
	{
		if (sessionId != Session.Id)
		{
			return;
		}

		_ = InvokeAsync(async () =>
		{
			if (_terminal is { } terminal)
			{
				await terminal.FocusAsync();
			}
		});
	}

	private Task SaveCommandAsync(string command) => Commands.SaveAsync(Session, command);

	private void SaveCommandWithDetails(string command) => Commands.SaveWithDetails(Session, command);

	private async Task FindAsync()
	{
		if (_terminal is not null)
		{
			await _terminal.OpenFindAsync();
		}
	}

	private async Task ClearAsync()
	{
		if (_terminal is not null)
		{
			await _terminal.ClearAsync();
		}
	}

	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey == TerminalSettings.SectionKey)
		{
			_ = InvokeAsync(() =>
			{
				ApplyTerminalSettings();
				StateHasChanged();
			});
		}
		else if (sectionKey == FileTransferSettings.SectionKey)
		{
			_ = InvokeAsync(() =>
			{
				_placement = Settings.Get<FileTransferSettings>().Placement;
				StateHasChanged();
			});
		}
	}
}
