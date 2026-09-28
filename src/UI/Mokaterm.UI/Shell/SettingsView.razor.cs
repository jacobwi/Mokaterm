using Microsoft.AspNetCore.Components;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.UI.Shell;

/// <summary>
/// Settings over the workspace: every <see cref="SettingsPageDescriptor"/> grouped in a searchable list, with the
/// selected page rendered next to it.
/// </summary>
public sealed partial class SettingsView : ComponentBase, IDisposable
{
	private string _query = "";
	private IReadOnlyList<SettingsPageDescriptor> _visiblePages = [];

	[Inject]
	private IUiContributions Contributions { get; set; } = default!;

	[Inject]
	private ShellState Shell { get; set; } = default!;

	/// <summary>The page from the shell state, or the first page when none was chosen or it no longer exists.</summary>
	private SettingsPageDescriptor? Selected =>
		Contributions.SettingsPages.FirstOrDefault(page => string.Equals(page.Id, Shell.SettingsPageId, StringComparison.OrdinalIgnoreCase))
		?? (Contributions.SettingsPages is [var first, ..] ? first : null);

	public void Dispose() => Shell.Changed -= OnShellChanged;

	protected override void OnInitialized()
	{
		_visiblePages = Contributions.SettingsPages;
		Shell.Changed += OnShellChanged;
	}

	private static string GroupName(SettingsPageGroup group) => group switch
	{
		SettingsPageGroup.Application => "Application",
		SettingsPageGroup.Security => "Security",
		SettingsPageGroup.Protocols => "Protocols",
		_ => "About",
	};

	private void OnQueryChanged(string query)
	{
		_query = query;
		string text = query.Trim();
		_visiblePages = text.Length == 0
			? Contributions.SettingsPages
			: [.. Contributions.SettingsPages.Where(page => Matches(page, text))];
	}

	private static bool Matches(SettingsPageDescriptor page, string text) =>
		page.Title.Contains(text, StringComparison.CurrentCultureIgnoreCase)
		|| (page.Description?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false)
		|| (page.Keywords?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false);

	private void OnShellChanged() => _ = InvokeAsync(StateHasChanged);
}
