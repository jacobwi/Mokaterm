using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Commands;

/// <summary>Saving and using commands from a session, shared by the terminal row buttons, the toolbar and the palette.</summary>
internal sealed class CommandSnippetActions
{
	private readonly ICommandSnippetStore _store;
	private readonly IUserInteraction _interaction;
	private readonly InputBroadcast _broadcast;
	private readonly ShellState _shell;
	private readonly CommandTemplateFiller _filler;
	private readonly ILogger<CommandSnippetActions> _logger;

	public CommandSnippetActions(
		ICommandSnippetStore store,
		IUserInteraction interaction,
		InputBroadcast broadcast,
		ShellState shell,
		CommandTemplateFiller filler,
		ILogger<CommandSnippetActions> logger)
	{
		_store = store;
		_interaction = interaction;
		_broadcast = broadcast;
		_shell = shell;
		_filler = filler;
		_logger = logger;
	}

	/// <summary>
	/// Raised with a session id after a row button or a dialog took focus from its terminal. The session view puts
	/// focus back, so the next keystroke goes to the shell on the other end.
	/// </summary>
	public event Action<Guid>? TerminalFocusRequested;

	/// <summary>Raised with a session id when something asks for its saved commands panel.</summary>
	public event Action<Guid>? CommandsRequested;

	/// <summary>
	/// The snippet the quick button saves: this login on this machine, named after the command itself, and never set
	/// to run on its own. The line already ran as it stands, so anything in it that would read as a placeholder is
	/// escaped and it types back exactly as it was.
	/// </summary>
	public static CommandSnippet CreateQuickSnippet(string command, HostProfile host, ConnectionProfile connection, bool isTransient)
	{
		ArgumentNullException.ThrowIfNull(host);
		ArgumentNullException.ThrowIfNull(connection);
		string text = command?.Trim() ?? "";
		return new CommandSnippet
		{
			Id = Guid.NewGuid(),
			Name = CommandSnippetName.Derive(text),
			Command = CommandTemplate.EscapeText(text),

			// A quick-connect session has no saved login to hang the command on, so it belongs to the machine.
			Scope = isTransient ? CommandSnippetScope.Host : CommandSnippetScope.Connection,
			Host = host.Address,
			ConnectionId = isTransient ? null : connection.Id,
			RunImmediately = false,
		};
	}

	public static CommandSnippetTarget TargetFor(ISessionHandle session)
	{
		ArgumentNullException.ThrowIfNull(session);
		return new CommandSnippetTarget
		{
			Host = session.Host.Address,
			ConnectionId = session.IsTransient ? null : session.Connection.Id,
		};
	}

	public ValueTask<IReadOnlyList<CommandSnippet>> ListAsync(ISessionHandle session, CancellationToken cancellationToken) =>
		_store.QueryAsync(TargetFor(session), cancellationToken);

	public void RequestTerminalFocus(Guid sessionId) => TerminalFocusRequested?.Invoke(sessionId);

	/// <summary>Saves the command as it is. The scope is this login, or this machine for a quick-connect session.</summary>
	public async Task SaveAsync(ISessionHandle session, string command)
	{
		ArgumentNullException.ThrowIfNull(session);
		if (string.IsNullOrWhiteSpace(command))
		{
			return;
		}

		try
		{
			CommandSnippet saved = await _store.SaveAsync(CreateQuickSnippet(command, session.Host, session.Connection, session.IsTransient));
			string where = saved.Scope == CommandSnippetScope.Connection ? session.Title : session.Host.Address;
			_interaction.Notify(NoticeSeverity.Success, $"Saved \"{saved.Name}\" for {where}.", "Command saved");
		}
		catch (VaultLockedException)
		{
			_interaction.Notify(NoticeSeverity.Warning, "Unlock the vault to save commands.");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogError(ex, "Saving a command from a terminal row failed");
			_interaction.Notify(NoticeSeverity.Error, "The command could not be saved.");
		}
		finally
		{
			// The row button took focus from the terminal; typing must go back to the remote shell.
			RequestTerminalFocus(session.Id);
		}
	}

	/// <summary>Opens the editor so the command can be named, tagged and scoped before it is saved.</summary>
	public void SaveWithDetails(ISessionHandle session, string command)
	{
		ArgumentNullException.ThrowIfNull(session);
		if (string.IsNullOrWhiteSpace(command))
		{
			return;
		}

		CommandSnippet draft = CreateQuickSnippet(command, session.Host, session.Connection, session.IsTransient);
		_shell.OpenEditor(new CommandSnippetEditorRequest
		{
			Snippet = draft,
			SessionId = session.Id,
			HostLabel = session.Host.Address,
			ConnectionLabel = session.IsTransient ? null : session.Title,
		});
	}

	/// <summary>Opens the session's saved commands panel, from the toolbar, the palette or the shortcut.</summary>
	public void OpenPicker(ISessionHandle session)
	{
		ArgumentNullException.ThrowIfNull(session);
		CommandsRequested?.Invoke(session.Id);
	}

	/// <summary>
	/// Sends a saved command to the session's terminal. With <paramref name="run"/> the line break goes too, which
	/// runs it. A command with placeholders asks for their values first, and Cancel there types nothing.
	/// </summary>
	public async Task ApplyAsync(ISessionHandle session, CommandSnippet snippet, bool run)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(snippet);
		if (session.Terminal is not { IsOpen: true })
		{
			_interaction.Notify(NoticeSeverity.Warning, "The session has no open terminal to type into.");
			return;
		}

		// Filled in once, before anything is typed, so every session that types together gets the same line.
		string? command = await _filler.FillAsync(CommandTemplate.Parse(snippet.Command), run ? "Run" : "Type");
		if (command is null)
		{
			return;
		}

		// The question can stay open for a while, and the session can drop meanwhile.
		if (session.Terminal is not { IsOpen: true } stream)
		{
			_interaction.Notify(NoticeSeverity.Warning, "The session has no open terminal to type into.");
			return;
		}

		string text = run ? command + "\r" : command;
		try
		{
			await stream.SendTextAsync(text);

			// Typing a saved command into every session that types together is the point of the whole feature, and this
			// path does not go through the terminal view's tap.
			foreach (ITerminalStream other in _broadcast.OthersFor(session.Id))
			{
				await other.SendTextAsync(text);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "Sending a saved command to a terminal failed");
			_interaction.Notify(NoticeSeverity.Error, "The command could not be sent to the terminal.");
			return;
		}

		try
		{
			await _store.MarkUsedAsync(snippet.Id);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The command is already typed; the use count only sorts the picker.
			_logger.LogDebug(ex, "Recording that a saved command was used failed");
		}
	}

	/// <summary>
	/// The snippet a command run saves: for its one login when it ran on one, for that machine when every login was on
	/// the same one, and for every session otherwise. Never set to run on its own, like the quick button's.
	/// </summary>
	public static CommandSnippet CreateRunSnippet(string command, IReadOnlyList<(HostProfile Host, ConnectionProfile Login)> targets)
	{
		ArgumentNullException.ThrowIfNull(targets);
		string text = command?.Trim() ?? "";
		CommandSnippetScope scope = targets.Count == 1
			? CommandSnippetScope.Connection
			: targets.Count > 1 && targets.Select(target => target.Host.Address).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
				? CommandSnippetScope.Host
				: CommandSnippetScope.Global;

		return new CommandSnippet
		{
			Id = Guid.NewGuid(),
			Name = CommandSnippetName.Derive(text),
			Command = text,
			Scope = scope,
			Host = scope == CommandSnippetScope.Global ? null : targets[0].Host.Address,
			ConnectionId = scope == CommandSnippetScope.Connection ? targets[0].Login.Id : null,
			RunImmediately = false,
		};
	}

	/// <summary>
	/// Opens the command run with a saved command, on the logins its scope names. Nothing is run until the user says so.
	/// </summary>
	public void RunOnSeveralHosts(CommandSnippet snippet)
	{
		ArgumentNullException.ThrowIfNull(snippet);
		if (!_shell.OpenEditor(new CommandRunEditorRequest { Snippet = snippet }))
		{
			_interaction.Notify(NoticeSeverity.Info, "Close the open dialog first.");
		}
	}

	/// <summary>
	/// Saves the command of a run, which is the only way a run's command is ever kept. The scope follows the logins it
	/// ran on (<see cref="CreateRunSnippet"/>).
	/// </summary>
	public async Task SaveFromRunAsync(string command, IReadOnlyList<(HostProfile Host, ConnectionProfile Login)> targets)
	{
		ArgumentNullException.ThrowIfNull(targets);
		if (string.IsNullOrWhiteSpace(command))
		{
			return;
		}

		try
		{
			CommandSnippet saved = await _store.SaveAsync(CreateRunSnippet(command, targets));
			string where = saved.Scope switch
			{
				CommandSnippetScope.Connection => targets[0].Login.GetTitle(targets[0].Host),
				CommandSnippetScope.Host => saved.Host ?? targets[0].Host.Address,
				_ => "every session",
			};

			_interaction.Notify(NoticeSeverity.Success, $"Saved \"{saved.Name}\" for {where}.", "Command saved");
		}
		catch (VaultLockedException)
		{
			_interaction.Notify(NoticeSeverity.Warning, "Unlock the vault to save commands.");
		}
		catch (ArgumentException ex)
		{
			// The store's own rules, such as one line per saved command, written for the user.
			_interaction.Notify(NoticeSeverity.Warning, ex.Message, "Command not saved");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogError("Saving the command of a run failed: {Failure}", LogSafe.Describe(ex));
			_interaction.Notify(NoticeSeverity.Error, "The command could not be saved.");
		}
	}
}
