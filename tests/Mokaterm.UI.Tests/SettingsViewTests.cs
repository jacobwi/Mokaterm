using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Storage;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

public sealed partial class SettingsViewTests
{
	[Fact]
	public async Task Render_EveryPageInTheList_CanBeReachedWithTheKeyboard()
	{
		string html = await RenderAsync();

		Assert.Equal(3, ListItemTabStop().Count(html));

		// Nothing was picked, so the first page shows and is the one marked as current.
		Match current = Assert.Single(CurrentPage().Matches(html));
		Assert.Contains("General", current.Value, StringComparison.Ordinal);
	}

	// Where the settings area has no room for the list beside the page, CSS shows this dropdown in its place, so it is
	// always there, named, and showing the page on screen.
	[Fact]
	public async Task Render_ThePagesAreAlsoADropdown_ShowingTheCurrentPage()
	{
		string html = await RenderAsync();

		Match dropdown = Assert.Single(PageDropdown().Matches(html));
		Assert.Equal("General", dropdown.Groups["page"].Value);
	}

	private static Task<string> RenderAsync() =>
		StaticRender.RenderAsync<SettingsView>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSettingsPage(Page("general", "General", SettingsPageGroup.Application));
				services.AddSettingsPage(Page("terminal", "Terminal", SettingsPageGroup.Application));
				services.AddSettingsPage(Page("ssh", "SSH", SettingsPageGroup.Protocols));
				services.AddSingleton<IAppDataStore, MemoryAppDataStore>();
				services.AddScoped<IUserInteraction, UserInteractionService>();
				services.AddScoped<UiStateStore>();
				services.AddScoped<ShellState>();
				services.AddScoped<ShellInterop>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal))
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

	private static SettingsPageDescriptor Page(string id, string title, SettingsPageGroup group) => new()
	{
		Id = id,
		Title = title,
		Icon = MokatermIcons.Sliders,
		Component = typeof(EmptyPage),
		Group = group,
	};

	[GeneratedRegex("<div class=\"moka-list-item[^\"]*\"[^>]*tabindex=\"0\"")]
	private static partial Regex ListItemTabStop();

	[GeneratedRegex("<div class=\"moka-list-item[^>]*aria-current=\"page\"[^>]*>.*?</div>", RegexOptions.Singleline)]
	private static partial Regex CurrentPage();

	[GeneratedRegex("role=\"combobox\"[^>]*aria-label=\"Settings page\"[^>]*>.*?mt-settings-option-text[^>]*>(?<page>[^<]*)<", RegexOptions.Singleline)]
	private static partial Regex PageDropdown();

	/// <summary>A settings page with nothing on it.</summary>
	private sealed class EmptyPage : ComponentBase
	{
		protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "page");
	}
}
