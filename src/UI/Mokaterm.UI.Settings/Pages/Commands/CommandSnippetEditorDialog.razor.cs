using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.UI.Settings.Pages.Commands;

/// <summary>
/// Names, tags and scopes a saved command. Used both from a terminal row and from the settings page, so the scopes
/// it offers come from the labels the caller passes in.
/// </summary>
public sealed partial class CommandSnippetEditorDialog : DraftDialogBase
{
	private CommandSnippet? _editing;
	private IList<string> _tags = [];
	private CommandSnippetScope _scope;
	private string _command = "";
	private string _name = "";
	private string? _commandError;
	private string? _error;
	private bool _run;
	private bool _saving;

	[Inject]
	private ICommandSnippetStore Store { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private ILogger<CommandSnippetEditorDialog> Logger { get; set; } = default!;

	/// <summary>
	/// The command being edited. A snippet whose <see cref="CommandSnippet.CreatedAt"/> is still default counts as
	/// new, which is how a row saved from a terminal arrives.
	/// </summary>
	[Parameter]
	public CommandSnippet? Snippet { get; set; }

	/// <summary>The machine to offer as a scope, or null when there is none to offer.</summary>
	[Parameter]
	public string? HostLabel { get; set; }

	/// <summary>The saved login to offer as a scope, or null when there is none to offer.</summary>
	[Parameter]
	public string? ConnectionLabel { get; set; }

	[Parameter]
	public EventCallback<CommandSnippet> OnSaved { get; set; }

	protected override bool IsBusy => _saving;

	private bool IsNew => _editing is null || _editing.CreatedAt == default;

	private string DialogTitle => IsNew ? "Save command" : "Edit command";

	private string ScopeHelp => _scope switch
	{
		CommandSnippetScope.Connection => ConnectionLabel is { } login ? $"Offered when you open {login}." : "Offered for one saved login.",
		CommandSnippetScope.Host => HostLabel is { } host ? $"Offered on {host}, over any login." : "Offered on one machine.",
		_ => "Offered in every session, on every machine.",
	};

	protected override void Load()
	{
		_editing = Snippet;
		_command = Snippet?.Command ?? "";
		_name = Snippet?.Name ?? "";
		_tags = [.. Snippet?.Tags ?? []];
		_run = Snippet?.RunImmediately ?? false;
		_scope = AllowedScope(Snippet?.Scope ?? CommandSnippetScope.Global);
	}

	// A command line can carry a token, so a closed dialog keeps none of it.
	protected override void Clear()
	{
		_editing = null;
		_command = "";
		_name = "";
		_tags = [];
		_run = false;
		_scope = CommandSnippetScope.Global;
		_commandError = null;
		_error = null;
		_saving = false;
	}

	private CommandSnippetScope AllowedScope(CommandSnippetScope scope) => scope switch
	{
		CommandSnippetScope.Connection when ConnectionLabel is null => AllowedScope(CommandSnippetScope.Host),
		CommandSnippetScope.Host when HostLabel is null => CommandSnippetScope.Global,
		_ => scope,
	};

	private void OnScopeChanged(string value)
	{
		if (Enum.TryParse(value, out CommandSnippetScope scope))
		{
			_scope = scope;
		}
	}

	private void OnRunChanged(bool run) => _run = run;

	private async Task SaveAsync()
	{
		if (_saving)
		{
			return;
		}

		_commandError = string.IsNullOrWhiteSpace(_command) ? "A command is needed." : null;
		_error = null;
		if (_commandError is not null)
		{
			return;
		}

		_saving = true;
		try
		{
			CommandSnippet snippet = (_editing ?? NewSnippet()) with
			{
				Name = _name.Trim(),
				Command = _command.Trim(),
				Tags = [.. _tags],
				Scope = _scope,
				RunImmediately = _run,
			};

			CommandSnippet saved = await Store.SaveAsync(snippet);
			_saving = false;
			await CloseAsync();
			if (OnSaved.HasDelegate)
			{
				await OnSaved.InvokeAsync(saved);
			}
		}
		catch (VaultLockedException)
		{
			_saving = false;
			_error = VaultFailure.Locked("save commands");
		}
		catch (ArgumentException ex)
		{
			_saving = false;
			_commandError = ex.Message;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Saving a command failed");
			_saving = false;
			_error = "The command could not be saved.";
			Interaction.Notify(NoticeSeverity.Error, "The command could not be saved.");
		}
	}

	private CommandSnippet NewSnippet() => new()
	{
		Id = Guid.NewGuid(),
		Name = "",
		Command = "",
		Host = Snippet?.Host,
		ConnectionId = Snippet?.ConnectionId,
	};
}
