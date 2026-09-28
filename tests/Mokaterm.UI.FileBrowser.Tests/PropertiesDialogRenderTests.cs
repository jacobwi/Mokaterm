using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.FileBrowser.Dialogs;
using Mokaterm.UI.FileBrowser.Extensions;
using Mokaterm.UI.FileBrowser.Properties;
using static Mokaterm.UI.FileBrowser.Tests.Fakes.FakeFileSystem;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>Renders the dialog for real, which catches markup that only breaks once a renderer walks it.</summary>
public sealed class PropertiesDialogRenderTests
{
	[Fact]
	public async Task Folder_ShowsEverySectionAndStartsWithApplyOff()
	{
		string html = await RenderAsync(
			[Folder("/srv/site", owner: "www-data", group: "www-data")],
			PropertiesSection.Permissions,
			RemoteFileSystemFeatures.Permissions | RemoteFileSystemFeatures.Ownership | RemoteFileSystemFeatures.Accounts);

		Assert.Contains(">General<", html, StringComparison.Ordinal);
		Assert.Contains(">Ownership<", html, StringComparison.Ordinal);
		Assert.Contains(">Permissions<", html, StringComparison.Ordinal);
		Assert.Contains(">Run as<", html, StringComparison.Ordinal);
		Assert.Contains("/srv/site", html, StringComparison.Ordinal);
		Assert.Contains("Calculate", html, StringComparison.Ordinal);
		Assert.Contains("rwxr-xr-x", html, StringComparison.Ordinal);
		Assert.Contains("value=\"755\"", html, StringComparison.Ordinal);
		Assert.Contains("data-dialog-focus", html, StringComparison.Ordinal);

		// The owner list arrived, so the field says which uid the name stands for.
		Assert.Contains("uid 33", html, StringComparison.Ordinal);
		Assert.Contains("Apply to everything inside", html, StringComparison.Ordinal);
		Assert.Contains("disabled title=\"Apply (Ctrl&#x2B;Enter)\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Selection_OfLinksOnly_IsReadOnly()
	{
		string html = await RenderAsync(
			[Link("/srv/current"), Link("/srv/previous")],
			PropertiesSection.General,
			RemoteFileSystemFeatures.Permissions | RemoteFileSystemFeatures.Ownership);

		Assert.Contains("2 items", html, StringComparison.Ordinal);
		Assert.Contains("Links keep their owner and permissions here", html, StringComparison.Ordinal);
		Assert.Contains(">Close<", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">Ownership<", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">Run as<", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task WithoutOwnershipAndPermissions_TheListingValuesAreStillShown()
	{
		string html = await RenderAsync([File("/srv/a.txt", size: 2048, mode: Mode("640"))], PropertiesSection.General, RemoteFileSystemFeatures.None);

		// The listing's owner and mode still show as text, with no fields to change them.
		Assert.Contains("2 KB (2,048 bytes)", html, StringComparison.Ordinal);
		Assert.Contains("-rw-r----- (640)", html, StringComparison.Ordinal);
		Assert.Contains("abc:abc", html, StringComparison.Ordinal);
		Assert.Contains(">Close<", html, StringComparison.Ordinal);
		Assert.DoesNotContain("<input", html, StringComparison.Ordinal);
	}

	private static async Task<string> RenderAsync(RemoteFileEntry[] entries, PropertiesSection section, RemoteFileSystemFeatures features)
	{
		RemoteAccounts accounts = new()
		{
			Users = [new RemoteAccount("root", 0), new RemoteAccount("www-data", 33)],
			Groups = [new RemoteAccount("root", 0), new RemoteAccount("www-data", 33)],
		};

		Dictionary<string, object?> parameters = new(StringComparer.Ordinal)
		{
			[nameof(PropertiesDialog.Entries)] = entries,
			[nameof(PropertiesDialog.Section)] = section,
			[nameof(PropertiesDialog.UserName)] = "abc",
			[nameof(PropertiesDialog.SupportsElevation)] = true,
			[nameof(PropertiesDialog.Features)] = features,
			[nameof(PropertiesDialog.LoadAccounts)] = (Func<CancellationToken, Task<RemoteAccounts>>)(_ => Task.FromResult(accounts)),
			[nameof(PropertiesDialog.MeasureSize)] = (Func<Action<TreeSize>, CancellationToken, Task<TreeSize>>)((_, _) => Task.FromResult(default(TreeSize))),
		};

		return await StaticRender.RenderAsync<PropertiesDialog>(
			services =>
			{
				// The path's CopyButton comes from UI.Common and copies through IClipboardService, which reads the
				// clipboard-clearing setting and reports a failure through IUserInteraction.
				services.AddSingleton<ISettingsService, FakeSettingsService>();
				services.AddScoped<IUserInteraction, DismissingInteraction>();
				services.AddMokatermUiCommon();
				services.AddMokatermFileBrowser();
			},
			parameters);
	}
}
