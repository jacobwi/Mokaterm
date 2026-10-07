using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.FileBrowser.Dialogs;
using Mokaterm.UI.FileBrowser.Extensions;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>
/// Renders the editor dialog for real. A static render reaches no browser, so the gutter is the empty element the page
/// fills and everything asserted here is what .NET draws on its own.
/// </summary>
public sealed class FileEditDialogRenderTests
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

	[Fact]
	public async Task ShowsThePathTheChipsAndWhereTheCaretIs()
	{
		string html = await RenderAsync("alpha\nbeta\ngamma\n", ["229 B", "UTF-8", "LF"]);

		Assert.Contains("/home/abc/notes.md", html, StringComparison.Ordinal);
		Assert.Contains("Copy path", html, StringComparison.Ordinal);

		// Each fact is its own chip rather than one run of text.
		Assert.Contains("moka-tag", html, StringComparison.Ordinal);
		Assert.Contains(">229 B<", html, StringComparison.Ordinal);
		Assert.Contains(">UTF-8<", html, StringComparison.Ordinal);
		Assert.Contains(">LF<", html, StringComparison.Ordinal);

		Assert.Contains("Ln 1, Col 1", html, StringComparison.Ordinal);

		// A trailing newline opens the line after it, which is the line the caret can reach.
		Assert.Contains("4 lines", html, StringComparison.Ordinal);
		Assert.Contains("Tab indents", html, StringComparison.Ordinal);
		Assert.Contains("Escape then Tab leaves the box", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheGutterAndTheBoxAreThereForThePageToWireUp()
	{
		string html = await RenderAsync("alpha", ["5 B", "UTF-8", "LF"]);

		Assert.Contains("mt-edit-gutter", html, StringComparison.Ordinal);
		Assert.Contains("mt-edit-numbers", html, StringComparison.Ordinal);
		Assert.Contains("aria-hidden=\"true\"", html, StringComparison.Ordinal);

		// The box starts focused, keeps Enter for typing, and never wraps, which is what lets one number be one row.
		Assert.Contains("data-dialog-focus", html, StringComparison.Ordinal);
		Assert.Contains("data-no-submit", html, StringComparison.Ordinal);
		Assert.Contains("wrap=\"off\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Contents of notes.md\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task NothingIsUnsavedUntilSomethingIsTypedAndSaveSaysItsKey()
	{
		string html = await RenderAsync("alpha", ["5 B", "UTF-8", "LF"]);

		Assert.Contains("1 line", html, StringComparison.Ordinal);
		Assert.DoesNotContain("UNSAVED", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mt-edit-frame--dirty", html, StringComparison.Ordinal);

		// Disabled while there is nothing to write, and the title is where Ctrl+S is spelled out.
		Assert.Contains("disabled title=\"Save (Ctrl&#x2B;S)\"", html, StringComparison.Ordinal);
		Assert.Contains(">Cancel<", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(string content, string[] facts)
	{
		Dictionary<string, object?> parameters = new(StringComparer.Ordinal)
		{
			[nameof(FileEditDialog.FileName)] = "notes.md",
			[nameof(FileEditDialog.Path)] = "/home/abc/notes.md",
			[nameof(FileEditDialog.Content)] = content,
			[nameof(FileEditDialog.Facts)] = facts,
			[nameof(FileEditDialog.Save)] = (Func<string, Task<string?>>)(_ => Task.FromResult<string?>(null)),
			[nameof(FileEditDialog.ConfirmDiscard)] = (Func<Task<bool>>)(() => Task.FromResult(true)),
		};

		return StaticRender.RenderAsync<FileEditDialog>(
			services =>
			{
				// The path's CopyButton copies through IClipboardService, which reads the clipboard-clearing setting
				// and reports a failure through IUserInteraction.
				services.AddSingleton<ISettingsService, FakeSettingsService>();
				services.AddScoped<IUserInteraction, DismissingInteraction>();
				services.AddMokatermUiCommon();
				services.AddMokatermFileBrowser();
			},
			parameters).WaitAsync(Limit, TestContext.Current.CancellationToken);
	}
}
