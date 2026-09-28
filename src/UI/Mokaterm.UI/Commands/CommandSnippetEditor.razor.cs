using Microsoft.AspNetCore.Components;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Commands;

/// <summary>Shows the command editor whenever the shell has one queued, like the host and login editors.</summary>
public sealed partial class CommandSnippetEditor : ShellEditorBase<CommandSnippetEditorRequest>
{
	private Guid? _focusSessionOnClose;

	[Inject]
	private CommandSnippetActions Actions { get; set; } = default!;

	/// <summary>The dialog does its own saving, so the shell side is never busy.</summary>
	protected override bool IsBusy => false;

	// The dialog releases its focus trap while it closes, so the terminal is asked for focus one render later.
	protected override void OnAfterRender(bool firstRender)
	{
		if (Request is null && _focusSessionOnClose is { } sessionId)
		{
			_focusSessionOnClose = null;
			Actions.RequestTerminalFocus(sessionId);
		}
	}

	/// <summary>The dialog reads the request straight off the parameters, so there is nothing to load.</summary>
	protected override Task LoadAsync(CommandSnippetEditorRequest request) => Task.CompletedTask;

	private void OnOpenChanged(bool open)
	{
		if (open || Request is not { } request)
		{
			return;
		}

		_focusSessionOnClose = request.SessionId;
		Close();
	}
}
