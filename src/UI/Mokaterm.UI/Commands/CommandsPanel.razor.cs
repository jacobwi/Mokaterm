using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Common.Commands;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Commands;

/// <summary>
/// The saved commands that apply to one session, docked beside its terminal. Enter types the selected command at the
/// prompt, Ctrl+Enter runs it, Escape goes back to the terminal.
/// </summary>
public sealed partial class CommandsPanel : ComponentBase, IDisposable
{
	private readonly CancellationTokenSource _lifetime = new();
	private IReadOnlyList<CommandSnippet> _snippets = [];
	private IReadOnlyList<CommandSnippet> _rows = [];
	private ISessionHandle? _loaded;
	private ElementReference _body;
	private ElementReference _list;
	private string _query = "";
	private string? _error;
	private bool _loading;
	private bool _focusPending;
	private bool _scrollPending;
	private bool _disposed;
	private int _selected;

	[Parameter, EditorRequired]
	public ISessionHandle Session { get; set; } = default!;

	/// <summary>Raised when the panel should close, after Escape or after a command was used.</summary>
	[Parameter]
	public EventCallback OnClosed { get; set; }

	[Inject]
	private ShellState Shell { get; set; } = default!;

	[Inject]
	private CommandSnippetActions Actions { get; set; } = default!;

	[Inject]
	private ICommandSnippetStore Store { get; set; } = default!;

	[Inject]
	private ShellInterop Interop { get; set; } = default!;

	[Inject]
	private FormInterop Forms { get; set; } = default!;

	[Inject]
	private ILogger<CommandsPanel> Logger { get; set; } = default!;

	/// <summary>Puts the keyboard in the search box, for the toolbar button and the shortcut that opened the panel.</summary>
	public void Focus()
	{
		_focusPending = true;
		StateHasChanged();
	}

	public void Dispose()
	{
		_disposed = true;
		Store.Changed -= OnStoreChanged;
		_lifetime.Cancel();
		_lifetime.Dispose();
	}

	protected override void OnInitialized()
	{
		Store.Changed += OnStoreChanged;
		_focusPending = true;
	}

	protected override void OnParametersSet()
	{
		if (!ReferenceEquals(_loaded, Session))
		{
			_query = "";
			_selected = 0;
			_ = LoadAsync();
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (_focusPending)
		{
			_focusPending = false;
			await Forms.FocusPreferredAsync(_body, selectText: true);
		}

		if (_scrollPending)
		{
			_scrollPending = false;
			await Interop.ScrollActiveIntoViewAsync(_list);
		}
	}

	private static MokaColor ScopeColor(CommandSnippetScope scope) => scope switch
	{
		CommandSnippetScope.Connection => MokaColor.Primary,
		CommandSnippetScope.Host => MokaColor.Info,
		_ => MokaColor.Surface,
	};

	private void OnStoreChanged() => _ = InvokeAsync(LoadAsync);

	private async Task LoadAsync()
	{
		// A store change can still arrive on the dispatcher after the panel closed.
		if (_disposed)
		{
			return;
		}

		ISessionHandle session = Session;
		_loaded = session;
		_loading = _snippets.Count == 0;
		_error = null;
		StateHasChanged();
		try
		{
			IReadOnlyList<CommandSnippet> snippets = await Actions.ListAsync(session, _lifetime.Token);
			if (!ReferenceEquals(_loaded, session))
			{
				return;
			}

			_snippets = snippets;
			_rows = CommandSnippetSearch.Filter(_snippets, _query);
			_selected = Math.Clamp(_selected, 0, Math.Max(0, _rows.Count - 1));
		}
		catch (OperationCanceledException)
		{
			return;
		}
		catch (VaultLockedException)
		{
			_error = "Unlock the vault to see saved commands.";
		}
		catch (Exception ex)
		{
			// Nobody awaits this load, so without a message here the panel would claim there are no commands.
			Logger.LogError(ex, "Loading the saved commands for a session failed");
			_error = "The saved commands could not be loaded.";
		}
		finally
		{
			if (ReferenceEquals(_loaded, session))
			{
				_loading = false;
				StateHasChanged();
			}
		}
	}

	private void OnQueryChanged(string query)
	{
		_query = query;
		_rows = CommandSnippetSearch.Filter(_snippets, query);
		_selected = 0;
		_scrollPending = _rows.Count > 0;
	}

	private async Task OnKeyDownAsync(KeyboardEventArgs args)
	{
		switch (args.Key)
		{
			case "ArrowDown":
				Select(_selected + 1);
				break;
			case "ArrowUp":
				Select(_selected - 1);
				break;
			case "Home":
				Select(0);
				break;
			case "End":
				Select(_rows.Count - 1);
				break;
			case "Enter":
				await ApplySelectedAsync(args.CtrlKey || args.MetaKey);
				break;
			case "Escape":
				await CloseAsync();
				break;
		}
	}

	private void Select(int index)
	{
		if (_rows.Count == 0)
		{
			return;
		}

		int next = Math.Clamp(index, 0, _rows.Count - 1);
		if (next == _selected)
		{
			return;
		}

		_selected = next;
		_scrollPending = true;
	}

	private Task ApplySelectedAsync(bool run) =>
		_selected >= 0 && _selected < _rows.Count ? ApplyAsync(_rows[_selected], run) : Task.CompletedTask;

	private async Task ApplyAsync(CommandSnippet snippet, bool run)
	{
		await Actions.ApplyAsync(Session, snippet, run || snippet.RunImmediately);

		// The command is at the prompt; typing belongs to the terminal again, with the panel left open to pick another.
		Actions.RequestTerminalFocus(Session.Id);
	}

	private Task CloseAsync()
	{
		Actions.RequestTerminalFocus(Session.Id);
		return OnClosed.InvokeAsync();
	}

	private void Manage() => Shell.OpenSettings("commands");
}
