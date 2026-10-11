namespace Mokaterm.Generators.Tests;

/// <summary>
/// What the settings search index holds for a page's markup. The real pages are checked against their rendered rows in
/// Mokaterm.UI.Tests; these cover the markup none of them happens to use yet.
/// </summary>
public sealed class SettingsSearchGeneratorTests
{
	private const string TerminalPage = "namespace Demo.Pages.Terminal { public partial class TerminalPage { } }";

	[Fact]
	public void LiteralRows_AreIndexedUnderTheirPageAndSection_InTheOrderThePageDrawsThem()
	{
		GeneratorRun run = GeneratorRun.Of("Pages/Terminal/TerminalPage.razor", """
			@inherits SettingsSectionBase<TerminalSettings>
			<SettingsPage Title="Terminal">
				<SettingsSection Title="Cursor">
					<SettingsRow Label="Style">
						<EnumSegmentedControl TEnum="TerminalCursorStyle" Value="settings.CursorStyle" ValueChanged="value => SaveAsync(s => s with { CursorStyle = value })" />
					</SettingsRow>
					<SettingsRow Label="Blink" Description="Blink the cursor while the terminal has focus.">
						<MokaSwitch Value="settings.CursorBlink" />
					</SettingsRow>
				</SettingsSection>
				<SettingsRow Label="Outside" Description="No section around it." />
			</SettingsPage>
			""", TerminalPage);

		Assert.Empty(run.Errors);
		Assert.Equal(
			[
				("Demo.Pages.Terminal.TerminalPage", "Cursor", "Style", null),
				("Demo.Pages.Terminal.TerminalPage", "Cursor", "Blink", "Blink the cursor while the terminal has focus."),
				("Demo.Pages.Terminal.TerminalPage", null, "Outside", "No section around it."),
			],
			run.Entries);
	}

	[Fact]
	public void ComputedLabel_IsLeftOut_AndAComputedDescriptionIsNull()
	{
		GeneratorRun run = GeneratorRun.Of("Pages/Terminal/TerminalPage.razor", """
			<SettingsRow Label="@DeviceUnlockLabel" Description="Unlock without typing the master password." />
			<SettingsRow Label="Status" Description="@StatusDescription" />
			<SettingsRow Label="Mixed" Description="Default port @port." />
			""", TerminalPage);

		Assert.Equal(
			[
				("Demo.Pages.Terminal.TerminalPage", null, "Status", null),
				("Demo.Pages.Terminal.TerminalPage", null, "Mixed", null),
			],
			run.Entries);
	}

	[Fact]
	public void CommentedOutRows_AndRowsThatAreNotSearchable_AreLeftOut()
	{
		GeneratorRun run = GeneratorRun.Of("Pages/Terminal/TerminalPage.razor", """
			@* <SettingsRow Label="Old razor comment" /> *@
			<!-- <SettingsRow Label="Old markup comment" /> -->
			<SettingsRow Label="No modules loaded" Searchable="false" />
			<SettingsRow Label="Kept" />
			""", TerminalPage);

		Assert.Equal("Kept", Assert.Single(run.Entries).Label);
	}

	[Fact]
	public void AtSigns_FollowRazor_AnAddressStaysAndADoubledOneIsOne()
	{
		GeneratorRun run = GeneratorRun.Of("Pages/Terminal/TerminalPage.razor", """
			<SettingsRow Label="Prompt colors" Description="Tells the parts of a user@host prompt apart." />
			<SettingsRow Label="@@mentions" />
			""", TerminalPage);

		Assert.Equal(
			[
				("Demo.Pages.Terminal.TerminalPage", null, "Prompt colors", "Tells the parts of a user@host prompt apart."),
				("Demo.Pages.Terminal.TerminalPage", null, "@mentions", null),
			],
			run.Entries);
	}

	[Fact]
	public void QuotesAndBracketsInsideAnExpression_DoNotEndTheTagEarly()
	{
		GeneratorRun run = GeneratorRun.Of("Pages/Terminal/TerminalPage.razor", """
			<SettingsRow Label="First" Description="@(wide ? "a > b" : $"{Name("c")} \" />")" Stacked>
				<MokaSwitch aria-label="@($"Turn {Name} on")" />
			</SettingsRow>
			<SettingsRow Label='Second' Description='Single quotes work too, "inner" ones included.' />
			""", TerminalPage);

		Assert.Equal(
			[
				("Demo.Pages.Terminal.TerminalPage", null, "First", null),
				("Demo.Pages.Terminal.TerminalPage", null, "Second", "Single quotes work too, \"inner\" ones included."),
			],
			run.Entries);
	}

	[Fact]
	public void TextIsWhatThePageShows_EntitiesDecodedAndWrappedLinesJoined()
	{
		GeneratorRun run = GeneratorRun.Of("Pages/Terminal/TerminalPage.razor", """
			<SettingsRow Label="Files &amp; transfers"
						 Description="A description wrapped
									  over two lines." />
			""", TerminalPage);

		Assert.Equal(("Demo.Pages.Terminal.TerminalPage", (string?)null, "Files & transfers", (string?)"A description wrapped over two lines."), Assert.Single(run.Entries));
	}

	[Fact]
	public void SectionTitle_ComesFromTheNearestOpenSection()
	{
		GeneratorRun run = GeneratorRun.Of("Pages/Terminal/TerminalPage.razor", """
			<SettingsSection Title="Outer">
				<SettingsSection Title="@Computed">
					<SettingsRow Label="Inner" />
				</SettingsSection>
				<SettingsRow Label="After" />
			</SettingsSection>
			<Mokaterm.UI.Common.Components.SettingsRow Label="Qualified" />
			""", TerminalPage);

		Assert.Equal(
			[
				("Demo.Pages.Terminal.TerminalPage", null, "Inner", null),
				("Demo.Pages.Terminal.TerminalPage", "Outer", "After", null),
				("Demo.Pages.Terminal.TerminalPage", null, "Qualified", null),
			],
			run.Entries);
	}

	[Fact]
	public void Namespace_FollowsTheFileTheImportsAboveItAndTheFolders()
	{
		GeneratorRun run = GeneratorRun.Of(
			[
				("Pages/Own/OwnPage.razor", "@namespace Elsewhere.Named\n<SettingsRow Label=\"Own\" />"),
				("Modules/_Imports.razor", "@using System\n@namespace Demo.Plugins"),
				("Modules/Ssh/Ssh-Page.razor", "<SettingsRow Label=\"Imported\" />"),
				("Root.razor", "<SettingsRow Label=\"Root\" />"),
			],
			"""
			namespace Elsewhere.Named { public partial class OwnPage { } }
			namespace Demo.Plugins.Ssh { public partial class Ssh_Page { } }
			namespace Demo { public partial class Root { } }
			""");

		// The page compiles, so every name is the one Razor gives it; files are written in path order.
		Assert.Empty(run.Errors);
		Assert.Equal(
			["Demo.Plugins.Ssh.Ssh_Page", "Elsewhere.Named.OwnPage", "Demo.Root"],
			run.Entries.Select(entry => entry.Page));
	}

	[Fact]
	public void GenericComponent_IsLeftOut()
	{
		GeneratorRun run = GeneratorRun.Of("Pages/ListPage.razor", "@typeparam TItem\n<SettingsRow Label=\"Rows\" />");

		Assert.Empty(run.Generated);
	}

	[Fact]
	public void ProjectThatCannotSeeTheAttribute_GetsNoOutput()
	{
		GeneratorRun run = GeneratorRun.Of([("Pages/Terminal/TerminalPage.razor", "<SettingsRow Label=\"Style\" />")], TerminalPage, withAttribute: false);

		Assert.Empty(run.Generated);
		Assert.Empty(run.Errors);
	}

	[Fact]
	public void MarkupWithoutRows_GetsNoOutput()
	{
		GeneratorRun run = GeneratorRun.Of("Pages/Terminal/TerminalPage.razor", "<MokaSwitch Value=\"true\" />", TerminalPage);

		Assert.Empty(run.Generated);
	}
}
