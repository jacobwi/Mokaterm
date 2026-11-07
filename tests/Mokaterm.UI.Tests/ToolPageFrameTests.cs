using Microsoft.AspNetCore.Components;
using Moka.Red.Extensions;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The frame every tool page is drawn on: its notices (the last change, a warning, a short form) sit under the header
/// and over the list and its panel alike, and stay while the first read still runs.
/// </summary>
public sealed class ToolPageFrameTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

	[Fact]
	public async Task Notices_SitBetweenTheHeaderAndTheList()
	{
		string html = await RenderAsync(loaded: true);

		int title = html.IndexOf(">Containers<", StringComparison.Ordinal);
		int notice = html.IndexOf("Restart web: exit 0", StringComparison.Ordinal);
		int list = html.IndexOf("the list", StringComparison.Ordinal);
		int detail = html.IndexOf("the panel", StringComparison.Ordinal);
		Assert.True(title >= 0 && title < notice && notice < list && list < detail, "The notice is not between the header and the list.");
	}

	[Fact]
	public async Task Notices_ShowWhileTheFirstReadRuns()
	{
		string html = await RenderAsync(loaded: false);

		Assert.Contains("Restart web: exit 0", html, StringComparison.Ordinal);
		Assert.DoesNotContain("the list", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(bool loaded) =>
		StaticRender.RenderAsync<ToolPageFrame>(
			services => services.AddMokaRed(),
			new Dictionary<string, object?>
			{
				[nameof(ToolPageFrame.Title)] = "Containers",
				[nameof(ToolPageFrame.Loaded)] = loaded,
				[nameof(ToolPageFrame.Loading)] = !loaded,
				[nameof(ToolPageFrame.ListPage)] = true,
				[nameof(ToolPageFrame.Notices)] = (RenderFragment)(builder => builder.AddContent(0, "Restart web: exit 0")),
				[nameof(ToolPageFrame.ChildContent)] = (RenderFragment)(builder => builder.AddContent(0, "the list")),
				[nameof(ToolPageFrame.Detail)] = (RenderFragment)(builder => builder.AddContent(0, "the panel")),
			})
			.WaitAsync(Wait, TestContext.Current.CancellationToken);
}
