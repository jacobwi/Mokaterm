using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The host editor says what each choice will look like rather than describing it: the row the tree will draw, the color
/// in force by name, the theme an environment resolves to, and the host that already answers to an address.
/// </summary>
public sealed partial class HostEditorTests
{
	private const string Address = "10.10.2.3";

	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private static readonly Guid HostId = Guid.Parse("2a7f3c18-0d24-4f3a-9c21-7b0f36b1c001");

	private static readonly Guid OtherHostId = Guid.Parse("2a7f3c18-0d24-4f3a-9c21-7b0f36b1c002");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Render_ANewHost_GroupsItsFieldsAndPreviewsAnUnnamedRow()
	{
		string html = await RenderAsync(ConnectionCatalog.Empty, new HostEditorRequest(null));

		Assert.Contains("Machine", html, StringComparison.Ordinal);
		Assert.Contains("Appearance", html, StringComparison.Ordinal);
		Assert.Contains("Notes", html, StringComparison.Ordinal);
		Assert.Contains("How this host reads in the tree, on its tabs and in the status bar.", html, StringComparison.Ordinal);

		// Nothing is typed yet, so the preview stands in for the name the tree will show.
		Assert.Contains("New host", html, StringComparison.Ordinal);

		// An untouched address field says nothing: the dialog opens without an error on it.
		Assert.DoesNotContain("Enter a hostname or IP address.", html, StringComparison.Ordinal);
	}

	// Clearing the color does not mean the host has none: it gets one from its address, and the dialog has to say so.
	[Fact]
	public async Task Render_AHostWithoutAColor_ChoosesAutomaticAndShowsTheColorItProduces()
	{
		string html = await RenderAsync(CatalogWith(Host()), new HostEditorRequest(HostId));

		Assert.Contains("Without one of its own, a host is drawn in a color taken from its address.", html, StringComparison.Ordinal);
		Assert.Equal(HostAccent.For(Address), ChosenSwatch(html));
	}

	[Fact]
	public async Task Render_AHostWithAPresetColor_NamesThePresetAndMarksItsSwatch()
	{
		string html = await RenderAsync(CatalogWith(Host(color: "#42a5f5")), new HostEditorRequest(HostId));

		Assert.Contains("Blue. Marks the host in the tree, on its tabs and in the status bar.", html, StringComparison.Ordinal);
		Assert.Equal("#42a5f5", ChosenSwatch(html));
	}

	// A color of its own is what the tree, the tabs and the status bar all draw, so the preview has to use the same rule.
	[Fact]
	public async Task Render_AHostWithAColor_DrawsThePreviewInIt()
	{
		string html = await RenderAsync(CatalogWith(Host(color: "#ce93d8")), new HostEditorRequest(HostId));

		Assert.Contains("background:#ce93d8", html, StringComparison.Ordinal);
		Assert.Contains("color:#ce93d8", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_AProductionHost_TagsThePreviewAndNamesTheThemeItsSessionsOpenIn()
	{
		string html = await RenderAsync(
			CatalogWith(Host(environment: HostEnvironment.Production)),
			new HostEditorRequest(HostId),
			new TerminalSettings { ProductionThemeId = "danger" },
			new OneTheme("danger", "Red Alert"));

		Assert.Contains("PRODUCTION", html, StringComparison.Ordinal);
		Assert.Contains("opens its sessions in the Red Alert theme", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_AProductionHostWithNoThemeForIt_PointsAtTheSettingsThatSetOne()
	{
		string html = await RenderAsync(CatalogWith(Host(environment: HostEnvironment.Production)), new HostEditorRequest(HostId));

		Assert.Contains("Settings, Terminal can also give it a theme of its own.", html, StringComparison.Ordinal);
		Assert.DoesNotContain("opens its sessions in the", html, StringComparison.Ordinal);
	}

	// The choice has to look like its result, so each segment carries the color its tag will be drawn in.
	[Fact]
	public async Task Render_TheEnvironments_CarryTheColorOfTheirOwnTag()
	{
		string html = await RenderAsync(ConnectionCatalog.Empty, new HostEditorRequest(null));

		foreach (HostEnvironment environment in HostEnvironmentDisplay.All)
		{
			Assert.Contains($"--mt-env:{HostEnvironmentDisplay.Accent(environment)}", html, StringComparison.Ordinal);
		}
	}

	[Fact]
	public async Task Render_AnAddressAnotherHostAlreadyUses_SaysWhichHostHasIt()
	{
		string html = await RenderAsync(
			CatalogWith(Host(), Host(OtherHostId, name: "Build box")),
			new HostEditorRequest(HostId));

		Assert.Contains("Build box already uses this address.", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ANameAnotherHostReadsAs_SaysBothRowsWillLookAlike()
	{
		string html = await RenderAsync(
			CatalogWith(Host(name: "Web"), Host(OtherHostId, address: "10.10.2.4", name: "web")),
			new HostEditorRequest(HostId));

		Assert.Contains("Another host is already shown as Web.", html, StringComparison.Ordinal);
	}

	// The host being edited is not its own duplicate, so reopening a saved host must stay quiet.
	[Fact]
	public async Task Render_TheOnlyHostWithItsAddress_SaysNothingAboutClashes()
	{
		string html = await RenderAsync(CatalogWith(Host(name: "Web")), new HostEditorRequest(HostId));

		Assert.DoesNotContain("already uses this address", html, StringComparison.Ordinal);
		Assert.DoesNotContain("already shown as", html, StringComparison.Ordinal);
	}

	private static HostProfile Host(
		Guid? id = null,
		string address = Address,
		string name = "",
		string? color = null,
		HostEnvironment environment = HostEnvironment.None) => new()
		{
			Id = id ?? HostId,
			Address = address,
			Name = name,
			Color = color,
			Environment = environment,
		};

	private static ConnectionCatalog CatalogWith(params HostProfile[] hosts) => new([], hosts, []);

	/// <summary>The color of the swatch the editor marks as chosen, which is a hex value or the automatic one.</summary>
	private static string? ChosenSwatch(string html)
	{
		foreach (Match button in Button().Matches(html))
		{
			if (!button.Value.Contains("mt-swatch", StringComparison.Ordinal)
				|| !button.Value.Contains("aria-pressed=\"true\"", StringComparison.Ordinal))
			{
				continue;
			}

			return SwatchColor().Match(button.Value) is { Success: true } color ? color.Groups[1].Value : null;
		}

		return null;
	}

	private static Task<string> RenderAsync(
		ConnectionCatalog catalog,
		HostEditorRequest request,
		TerminalSettings? terminal = null,
		ITerminalThemeCatalog? themes = null)
	{
		ShellState shell = new(new UiStateStore(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance));
		shell.OpenEditor(request);

		return StaticRender.RenderAsync<HostEditor>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<IAppDataStore, MemoryAppDataStore>();
				services.AddSingleton<IVault, UnlockedVault>();
				services.AddSingleton<IConnectionRepository>(new EditableCatalog(catalog));
				services.AddSingleton<ISettingsService>(new FixedSettings(terminal ?? new TerminalSettings()));
				services.AddSingleton(themes ?? new NoThemes());
				services.AddSingleton(shell);
				services.AddScoped<CatalogState>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)).WaitAsync(Patience, Ct);
	}

	[GeneratedRegex("<button[^>]*>")]
	private static partial Regex Button();

	[GeneratedRegex("--mt-swatch:([^\"]+)\"")]
	private static partial Regex SwatchColor();

	/// <summary>No environment names a theme here, so a catalog the editor must not reach proves it never does.</summary>
	private sealed class NoThemes : ITerminalThemeCatalog
	{
		public TerminalTheme Default => throw new NotSupportedException();

		public IReadOnlyList<TerminalTheme> Themes => throw new NotSupportedException();

		public TerminalTheme Get(string? id) => throw new NotSupportedException();
	}
}
