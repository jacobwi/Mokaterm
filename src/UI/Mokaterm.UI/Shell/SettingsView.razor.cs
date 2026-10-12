using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.UI.Shell;

/// <summary>
/// Settings over the workspace: every <see cref="SettingsPageDescriptor"/> grouped in a list, with the selected page
/// rendered next to it. Typing in the search box swaps the list for the pages and rows that match, rows included
/// through <see cref="SettingsSearchIndex"/>; opening a row scrolls it into view and makes it glow for a moment.
/// </summary>
public sealed partial class SettingsView : ComponentBase, IDisposable
{
	private string _query = "";
	private IReadOnlyList<SettingsSearchResult> _results = [];
	private int _active;
	private bool _scrollPending;
	private (bool Pending, string? Anchor) _reveal;
	private ElementReference _list;
	private ElementReference _content;

	[Inject]
	private IUiContributions Contributions { get; set; } = default!;

	[Inject]
	private ShellState Shell { get; set; } = default!;

	[Inject]
	private ShellInterop Interop { get; set; } = default!;

	/// <summary>The page from the shell state, or the first page when none was chosen or it no longer exists.</summary>
	private SettingsPageDescriptor? Selected =>
		Contributions.SettingsPages.FirstOrDefault(page => string.Equals(page.Id, Shell.SettingsPageId, StringComparison.OrdinalIgnoreCase))
		?? (Contributions.SettingsPages is [var first, ..] ? first : null);

	private bool Searching => _query.Trim().Length > 0;

	public void Dispose() => Shell.Changed -= OnShellChanged;

	protected override void OnInitialized() => Shell.Changed += OnShellChanged;

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (_scrollPending)
		{
			_scrollPending = false;
			await Interop.ScrollActiveIntoViewAsync(_list);
		}

		if (_reveal.Pending)
		{
			string? anchor = _reveal.Anchor;
			_reveal = default;
			await Interop.RevealSettingAsync(_content, anchor);
		}
	}

	private static string GroupName(SettingsPageGroup group) => group switch
	{
		SettingsPageGroup.Application => "Application",
		SettingsPageGroup.Security => "Security",
		SettingsPageGroup.Protocols => "Protocols",
		SettingsPageGroup.Plugins => "Plugins",
		_ => "About",
	};

	private void OnQueryChanged(string query)
	{
		_query = query;
		_results = SettingsSearch.Find(Contributions.SettingsPages, query, SettingsSearchIndex.For);
		_active = 0;
		_scrollPending = _results.Count > 0;
	}

	private void OnSearchKeyDown(KeyboardEventArgs args)
	{
		switch (args.Key)
		{
			case "ArrowDown":
				MoveTo(_active + 1);
				break;
			case "ArrowUp":
				MoveTo(_active - 1);
				break;
			case "Enter":
				Open(_active);
				break;
			case "Escape":
				OnQueryChanged("");
				break;
		}
	}

	private void MoveTo(int index)
	{
		if (_results.Count == 0)
		{
			return;
		}

		_active = Math.Clamp(index, 0, _results.Count - 1);
		_scrollPending = true;
	}

	/// <summary>
	/// Shows the result's page and its row. The results stay, so the next one is an arrow key away, and the page is
	/// revealed after the render that puts it on screen.
	/// </summary>
	private void Open(int index)
	{
		if (index < 0 || index >= _results.Count)
		{
			return;
		}

		SettingsSearchResult result = _results[index];
		_active = index;
		_reveal = (true, result.Anchor);
		Shell.SelectSettingsPage(result.Page.Id);
	}

	private void OnShellChanged() => _ = InvokeAsync(StateHasChanged);
}
