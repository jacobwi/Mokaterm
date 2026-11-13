using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Tests;

/// <summary>
/// Safari, iOS and Firefox take a clipboard write only inside the click, so the copy button carries the mark that makes
/// the page start one there, and the text still comes from .NET rather than sitting in the page.
/// </summary>
public sealed partial class CopyButtonTests
{
	[Fact]
	public async Task TheButton_StartsItsWriteInsideTheClick_WithoutTheTextInThePage()
	{
		string html = await StaticRender.RenderAsync<CopyButton>(
			services =>
			{
				services.AddMokaRed();
				services.AddSingleton(TimeProvider.System);
				services.AddSingleton<IClipboardService, UnusedClipboard>();
				services.AddSingleton<IUserInteraction, RecordingInteraction>();
			},
			new()
			{
				[nameof(CopyButton.Text)] = "SHA256:3q2+7wAbCdEfGhIjKlMnOpQrStUvWxYz0123456789",
				[nameof(CopyButton.Label)] = "Copy fingerprint",
			});

		Match button = ButtonStart().Match(html);
		Assert.True(button.Success, html);
		Assert.Contains(CopyClick.Class, button.Groups["class"].Value.Split(' '));
		Assert.DoesNotContain("3q2+7w", html, StringComparison.Ordinal);
	}

	[GeneratedRegex("""<button[^>]*\bclass="(?<class>[^"]*)""")]
	private static partial Regex ButtonStart();

	private sealed class UnusedClipboard : IClipboardService
	{
		public ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("Nothing is clicked in a static render.");

		public ValueTask<string?> ReadTextAsync(CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("Nothing is clicked in a static render.");

		public ValueTask WriteSecretAsync(string secret, CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("Nothing is clicked in a static render.");
	}
}
