using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Commands;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Interaction;

namespace Mokaterm.UI.Tests;

/// <summary>What the placeholder question shows: a field per name, started from its value, and the command it makes.</summary>
public sealed partial class PlaceholderPromptDialogTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task OneFieldPerName_StartedFromTheDefault_WithTheFirstOneFocused()
	{
		string html = await RenderAsync(new PlaceholderPrompt
		{
			Title = "Fill in the command",
			Template = CommandTemplate.Parse("ssh -p {{port:22}} {{host}} -- ls {{host}}"),
			ConfirmText = "Type",
		});

		MatchCollection inputs = Input().Matches(html);
		Assert.Equal(2, inputs.Count);
		Assert.Contains("value=\"22\"", inputs[0].Value, StringComparison.Ordinal);
		Assert.Contains("data-autofocus=\"true\"", inputs[0].Value, StringComparison.Ordinal);
		Assert.DoesNotContain("data-autofocus", inputs[1].Value, StringComparison.Ordinal);
		Assert.Contains(">port<", html, StringComparison.Ordinal);
		Assert.Contains(">host<", html, StringComparison.Ordinal);
		Assert.Contains(">Type<", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ThePreview_IsTheCommandWithTheValuesIn()
	{
		string html = await RenderAsync(new PlaceholderPrompt
		{
			Title = "Fill in the command",
			Template = CommandTemplate.Parse("docker logs --tail {{lines:100}} {{name}} --format '{{.Names}}'"),
			Values = new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "web-1" },
			ConfirmText = "Run",
		});

		Assert.Contains("<pre class=\"mt-prompt-details\"", html, StringComparison.Ordinal);
		Assert.Contains("docker logs --tail 100 web-1 --format &#x27;{{.Names}}&#x27;</pre>", html, StringComparison.Ordinal);
		Assert.DoesNotContain("disabled", PrimaryButton().Match(html).Value, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AValueWithAControlCharacter_IsRefusedInItsField_AndTheCommandCannotGo()
	{
		string html = await RenderAsync(new PlaceholderPrompt
		{
			Title = "Fill in the command",
			Template = CommandTemplate.Parse("echo {{text}}"),
			Values = new Dictionary<string, string>(StringComparer.Ordinal) { ["text"] = "one\ttwo" },
			ConfirmText = "Run",
		});

		Assert.Contains("A value cannot hold a line break, a tab or another control character.", html, StringComparison.Ordinal);
		Assert.Contains("disabled", PrimaryButton().Match(html).Value, StringComparison.Ordinal);

		// The preview leaves the refused character out rather than drawing it.
		Assert.Contains("echo onetwo</pre>", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(PlaceholderPrompt prompt) =>
		StaticRender.RenderAsync<PlaceholderPromptDialog>(
			services =>
			{
				services.AddMokaRed();
				services.AddScoped<FormInterop>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[nameof(PlaceholderPromptDialog.Prompt)] = prompt,
				[nameof(PlaceholderPromptDialog.OnResult)] = EventCallback.Factory.Create<IReadOnlyDictionary<string, string>?>(new object(), _ => { }),
			})
			.WaitAsync(TimeSpan.FromSeconds(5), Ct);

	[GeneratedRegex("<input[^>]*>")]
	private static partial Regex Input();

	[GeneratedRegex("<button[^>]*data-primary=\"true\"[^>]*>")]
	private static partial Regex PrimaryButton();
}
