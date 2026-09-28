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
		string html = await StaticRender.RenderAsync<SettingsView>(
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
			},
			new Dictionary<string, object?>(StringComparer.Ordinal));

		Assert.Equal(3, ListItemTabStop().Count(html));

		// Nothing was picked, so the first page shows and is the one marked as current.
		Match current = Assert.Single(CurrentPage().Matches(html));
		Assert.Contains("General", current.Value, StringComparison.Ordinal);
	}

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

	/// <summary>A settings page with nothing on it.</summary>
	private sealed class EmptyPage : ComponentBase
	{
		protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "page");
	}
}
