using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.FileBrowser.Dialogs;
using Mokaterm.UI.FileBrowser.Editing;
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

	[Fact]
	public async Task ReviewChangesWaitsForAnEdit_AndNoDiffIsShownYet()
	{
		string html = await RenderAsync("alpha", ["5 B", "UTF-8", "LF"]);

		Assert.Contains("Review changes", html, StringComparison.Ordinal);
		Assert.Contains("disabled title=\"Compare the edits with the file as it was read\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mt-diff", html, StringComparison.Ordinal);
		Assert.DoesNotContain("inert", html, StringComparison.Ordinal);

		// A conflict's own buttons only appear with a conflict.
		Assert.DoesNotContain("Overwrite", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Back to editing", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheColouredLayerSitsUnderTheBox_AndTheStatusBarNamesTheLanguage()
	{
		string html = await RenderAsync("# Notes\n", ["8 B", "UTF-8", "LF"]);

		// Hidden from assistive technology: the box is what is read.
		Assert.Contains("<div class=\"mt-edit-paint\" aria-hidden=\"true\"", html, StringComparison.Ordinal);
		Assert.Contains("mt-edit-lines", html, StringComparison.Ordinal);
		Assert.True(
			html.IndexOf("mt-edit-paint", StringComparison.Ordinal) < html.IndexOf("mt-edit-area", StringComparison.Ordinal),
			"The layer has to come before the box, which paints over it.");

		Assert.Contains("aria-label=\"Highlighting: Markdown\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-haspopup=\"menu\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Too large to highlight", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("/srv/app/Dockerfile", "FROM alpine\n", "Dockerfile")]
	[InlineData("/usr/local/bin/backup", "#!/bin/sh\necho hi\n", "Shell")]
	[InlineData("/srv/app/notes.txt", "hello\n", "Plain text")]
	public async Task TheLanguageComesFromTheFile(string path, string content, string expected)
	{
		string html = await RenderAsync(content, ["UTF-8", "LF"], path);

		Assert.Contains($"aria-label=\"Highlighting: {expected}\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task PastThePaintCap_TheStatusBarSaysColouringStopped()
	{
		string content = string.Concat(Enumerable.Repeat("x = 1\n", FileEditDialog.MaxPaintLines + 1));

		string html = await RenderAsync(content, ["UTF-8", "LF"], "/srv/app/big.py");

		Assert.Contains("Too large to highlight", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task PlainTextPastThePaintCap_SaysNothingAboutColours()
	{
		string content = new('x', FileEditDialog.MaxPaintCharacters + 1);

		string html = await RenderAsync(content, ["UTF-8", "LF"], "/srv/app/big.txt");

		Assert.DoesNotContain("Too large to highlight", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(string content, string[] facts, string path = "/home/abc/notes.md")
	{
		Dictionary<string, object?> parameters = new(StringComparer.Ordinal)
		{
			[nameof(FileEditDialog.FileName)] = RemotePath.GetName(path),
			[nameof(FileEditDialog.Path)] = path,
			[nameof(FileEditDialog.Content)] = content,
			[nameof(FileEditDialog.Facts)] = facts,
			[nameof(FileEditDialog.Save)] = (Func<string, bool, Task<FileSaveResult>>)((_, _) => Task.FromResult(FileSaveResult.Saved)),
			[nameof(FileEditDialog.AcceptServerCopy)] = (Action)(() => { }),
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
