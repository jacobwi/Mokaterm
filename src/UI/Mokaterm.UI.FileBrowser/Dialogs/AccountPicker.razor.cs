using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Interop;
using Mokaterm.UI.FileBrowser.Properties;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>
/// A text field for a user or group name with a list of the server's accounts under it. Typing filters the list, a click
/// or Down opens it, Enter or a click picks the highlighted account, and any other name can still be typed.
/// </summary>
public sealed partial class AccountPicker : ComponentBase
{
	// Past this many the list asks for more letters instead of rendering thousands of directory accounts.
	private const int MaxShown = 100;

	private const string HelperStyle = "color:var(--moka-color-on-surface-variant)";

	private readonly string _listId = "ap-" + Guid.NewGuid().ToString("N");
	private ElementReference _listRef;
	private int _scrolledHighlight = -1;
	private IReadOnlyList<RemoteAccount>? _searched;
	private List<RemoteAccount> _matches = [];
	private string _filter = "";
	private bool _open;
	private int _highlight = -1;

	[Parameter, EditorRequired]
	public string Label { get; set; } = "";

	[Parameter]
	public string Value { get; set; } = "";

	[Parameter]
	public EventCallback<string> ValueChanged { get; set; }

	/// <summary>The accounts to offer. Empty makes this a plain text field.</summary>
	[Parameter]
	public IReadOnlyList<RemoteAccount> Accounts { get; set; } = [];

	/// <summary>Shown before each id in the list, such as <c>uid</c>.</summary>
	[Parameter]
	public string IdLabel { get; set; } = "id";

	[Parameter]
	public string? Placeholder { get; set; }

	[Parameter]
	public string? HelperText { get; set; }

	[Parameter]
	public string? ErrorText { get; set; }

	/// <summary>The dialog focuses this field when it opens.</summary>
	[Parameter]
	public bool Autofocus { get; set; }

	[Inject]
	private FileBrowserInterop Interop { get; set; } = default!;

	private bool ShowList => _open && _matches.Count > 0;

	private int ShownCount => Math.Min(_matches.Count, MaxShown);

	private string MoreText => string.Create(CultureInfo.CurrentCulture, $"{_matches.Count - MaxShown:N0} more, type to narrow the list");

	protected override void OnParametersSet()
	{
		// Accounts usually arrive after the first render, and an open list should pick them up. Other renders of the dialog
		// leave the list as the user left it.
		if (_open && !ReferenceEquals(Accounts, _searched))
		{
			Search(_filter);
			_highlight = Math.Min(_highlight, _matches.Count - 1);
		}
	}

	private string OptionClass(int position) => position == _highlight ? "ap-option ap-option--active" : "ap-option";

	private void Search(string filter)
	{
		_filter = filter;
		_searched = Accounts;
		_matches = AccountSearch.Find(Accounts, filter);
	}

	private async Task OnInputAsync(string value)
	{
		await ValueChanged.InvokeAsync(value);
		Search(value);
		_open = true;
		_highlight = value.Trim().Length > 0 && _matches.Count > 0 ? 0 : -1;
	}

	private void Toggle()
	{
		if (_open)
		{
			Close();
		}
		else
		{
			OpenAll();
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (!ShowList || _highlight < 0)
		{
			_scrolledHighlight = -1;
			return;
		}

		if (_highlight != _scrolledHighlight)
		{
			_scrolledHighlight = _highlight;
			try
			{
				_ = await Interop.ScrollSelectedIntoViewAsync(_listRef);
			}
			catch (JSException)
			{
				// The list is gone or the page is closing; the highlight still moved.
			}
		}
	}

	/// <summary>Opens with every account, the current one highlighted, so another can be picked without clearing the field.</summary>
	private void OpenAll()
	{
		Search("");
		_highlight = _matches.FindIndex(account => string.Equals(account.Name, Value.Trim(), StringComparison.Ordinal));
		_open = true;
	}

	private void Close()
	{
		_open = false;
		_highlight = -1;
	}

	private async Task OnKeyDownAsync(KeyboardEventArgs args)
	{
		switch (args.Key)
		{
			case "ArrowDown":
				if (!ShowList)
				{
					OpenAll();
				}
				else
				{
					_highlight = Math.Min(_highlight + 1, ShownCount - 1);
				}

				break;
			case "ArrowUp":
				if (ShowList)
				{
					_highlight = Math.Max(_highlight - 1, 0);
				}

				break;
			case "Enter":
				if (ShowList && _highlight >= 0 && _highlight < ShownCount)
				{
					await PickAsync(_matches[_highlight]);
				}
				else
				{
					Close();
				}

				break;
			case "Escape":
			case "Tab":
				Close();
				break;
		}
	}

	private async Task PickAsync(RemoteAccount account)
	{
		Close();
		await ValueChanged.InvokeAsync(account.Name);
	}
}
