using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Tests.Shared;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Interaction;

namespace Mokaterm.UI.Tests;

/// <summary>The check box that turns a prompt off is drawn only for a prompt that offered one.</summary>
public sealed class ConfirmPromptDialogTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task APromptThatCanBeTurnedOff_DrawsTheCheckBox()
	{
		string html = await RenderAsync(new ConfirmPrompt
		{
			Title = "Close session",
			Message = "bc@build-01 is still connected.",
			ConfirmText = "Close",
			RememberText = "Don't ask again",
		});

		Assert.Contains("Don&#x27;t ask again", html, StringComparison.Ordinal);
		Assert.Contains("type=\"checkbox\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task EveryOtherPrompt_DrawsNoCheckBox()
	{
		string html = await RenderAsync(new ConfirmPrompt
		{
			Title = "Delete key",
			Message = "This cannot be undone.",
			ConfirmText = "Delete",
			Destructive = true,
		});

		Assert.Contains("This cannot be undone.", html, StringComparison.Ordinal);
		Assert.DoesNotContain("type=\"checkbox\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Details_KeepTheirLineBreaks()
	{
		// The whole point of the paste prompt is seeing the lines, which a paragraph would run together.
		string html = await RenderAsync(new ConfirmPrompt
		{
			Title = "Paste with line breaks?",
			Message = "The text has 3 lines, and each line break can run a command.",
			Details = "cd /srv\nsudo systemctl restart api\ntail -f log",
			ConfirmText = "Paste",
		});

		Assert.Contains("<pre", html, StringComparison.Ordinal);
		// Blazor writes the line breaks as &#xA;, which the parser turns back into newlines for the pre-wrap box.
		Assert.Contains("cd /srv&#xA;sudo systemctl restart api&#xA;tail -f log", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task NoDetails_DrawNoBox()
	{
		string html = await RenderAsync(new ConfirmPrompt
		{
			Title = "Close session",
			Message = "bc@build-01 is still connected.",
			ConfirmText = "Close",
		});

		Assert.DoesNotContain("<pre", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(ConfirmPrompt prompt) =>
		StaticRender.RenderAsync<ConfirmPromptDialog>(
			services =>
			{
				services.AddMokaRed();
				services.AddScoped<FormInterop>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["Prompt"] = prompt })
			.WaitAsync(TimeSpan.FromSeconds(5), Ct);
}
