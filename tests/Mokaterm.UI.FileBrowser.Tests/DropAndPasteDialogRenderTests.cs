using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.UI.Common.Interaction;
using Mokaterm.UI.FileBrowser.Dialogs;
using Mokaterm.UI.FileBrowser.Extensions;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>Renders the drop and paste questions for real, which catches markup that only breaks once a renderer walks it.</summary>
public sealed class DropAndPasteDialogRenderTests
{
	private static readonly string[] TwoNames = ["app.log", "conf"];
	private static readonly string[] OneName = ["notes.txt"];

	[Fact]
	public async Task UploadQuestion_OffersDontAskAgain_WhenTheSettingIsOn()
	{
		string html = await RenderAsync<UploadConfirmDialog>(UploadParameters(asRoot: false, offerOptOut: true));

		Assert.Contains(PromptOptOut.Label.Replace("'", "&#x27;", StringComparison.Ordinal), html, StringComparison.Ordinal);
		Assert.Contains("abc@box:/srv/www", html, StringComparison.Ordinal);
	}

	// A drop as root asks either way, so the check box would promise something the next root drop does not do.
	[Fact]
	public async Task UploadQuestion_AsRoot_LeavesDontAskAgainOut()
	{
		string html = await RenderAsync<UploadConfirmDialog>(UploadParameters(asRoot: true, offerOptOut: true));

		Assert.DoesNotContain("ask again", html, StringComparison.Ordinal);
		Assert.Contains("Upload as root", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task UploadQuestion_AskedWithTheSettingOff_LeavesDontAskAgainOut()
	{
		string html = await RenderAsync<UploadConfirmDialog>(UploadParameters(asRoot: false, offerOptOut: false));

		Assert.DoesNotContain("ask again", html, StringComparison.Ordinal);
	}

	// Overwriting is destructive, so the paste question never offers to stop asking, and Enter keeps both.
	[Fact]
	public async Task PasteQuestion_ListsTheTakenNamesAndDefaultsToKeepBoth()
	{
		string html = await RenderAsync<PasteConflictDialog>(new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			[nameof(PasteConflictDialog.Names)] = TwoNames,
			[nameof(PasteConflictDialog.Directory)] = "/srv/b",
		});

		Assert.Contains("2 of the pasted items already exist in /srv/b.", html, StringComparison.Ordinal);
		Assert.Contains(">app.log<", html, StringComparison.Ordinal);
		Assert.Contains(">conf<", html, StringComparison.Ordinal);
		Assert.Contains(">Overwrite<", html, StringComparison.Ordinal);
		Assert.DoesNotContain("ask again", html, StringComparison.Ordinal);

		// The button Enter presses is the last one, Keep both.
		int defaultButton = html.LastIndexOf("data-dialog-default", StringComparison.Ordinal);
		Assert.True(defaultButton >= 0);
		Assert.Contains("Keep both", html[defaultButton..], StringComparison.Ordinal);
		Assert.DoesNotContain(">Overwrite<", html[defaultButton..], StringComparison.Ordinal);
	}

	[Fact]
	public async Task PasteQuestion_OneName_IsNamedInTheSentence()
	{
		string html = await RenderAsync<PasteConflictDialog>(new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			[nameof(PasteConflictDialog.Names)] = OneName,
			[nameof(PasteConflictDialog.Directory)] = "/home/abc",
		});

		Assert.Contains("&quot;notes.txt&quot; already exists in /home/abc.", html, StringComparison.Ordinal);
	}

	private static Dictionary<string, object?> UploadParameters(bool asRoot, bool offerOptOut) => new(StringComparer.Ordinal)
	{
		[nameof(UploadConfirmDialog.FileCount)] = 2,
		[nameof(UploadConfirmDialog.FolderCount)] = 0,
		[nameof(UploadConfirmDialog.TotalBytes)] = 2048L,
		[nameof(UploadConfirmDialog.Directory)] = "/srv/www",
		[nameof(UploadConfirmDialog.Host)] = "box",
		[nameof(UploadConfirmDialog.UserName)] = "abc",
		[nameof(UploadConfirmDialog.SupportsElevation)] = true,
		[nameof(UploadConfirmDialog.AsRoot)] = asRoot,
		[nameof(UploadConfirmDialog.OfferOptOut)] = offerOptOut,
	};

	private static Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
		where TComponent : IComponent =>
		StaticRender.RenderAsync<TComponent>(services => services.AddMokatermFileBrowser(), parameters);
}
