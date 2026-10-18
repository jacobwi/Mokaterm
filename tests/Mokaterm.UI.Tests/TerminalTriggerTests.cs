using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Settings.Pages.Triggers;
using Mokaterm.UI.Terminal.Interop;
using Mokaterm.UI.Terminal.Triggers;

namespace Mokaterm.UI.Tests;

public sealed class TerminalTriggerTests
{
	private static readonly TerminalTheme Theme = new Fakes.OneTheme("test", "Test").Get("test");

	[Fact]
	public void Palette_HasAColorForEveryChoice_AndNoneForNone()
	{
		Assert.Null(TriggerPalette.Hex(TriggerColor.None));
		Assert.Null(TriggerPalette.Hex((TriggerColor)99));
		Assert.All(TriggerPalette.Colors, color => Assert.Matches("^#[0-9a-f]{6}$", TriggerPalette.Hex(color)));
		Assert.Equal(TriggerPalette.Colors.Count, TriggerPalette.Colors.Select(TriggerPalette.Hex).Distinct().Count());
	}

	[Fact]
	public void JsTrigger_CarriesThePatternAndTheResolvedColor()
	{
		TerminalJsTrigger trigger = TerminalJsTrigger.From(new TerminalTrigger
		{
			Pattern = "ERROR|FATAL",
			IgnoreCase = true,
			Highlight = TriggerColor.Red,
			Notify = true,
		});

		Assert.Equal(new TerminalJsTrigger("ERROR|FATAL", true, "#ef5350", true), trigger);
	}

	[Fact]
	public void Options_TheSameRulesRebuilt_AreNotSentAgain()
	{
		TerminalJsOptions before = TerminalJsOptions.From(new TerminalSettings(), Theme, triggers: [new TerminalJsTrigger("x", false, "#ef5350", false)]);
		TerminalJsOptions after = TerminalJsOptions.From(new TerminalSettings(), Theme, triggers: [new TerminalJsTrigger("x", false, "#ef5350", false)]);

		Assert.DoesNotContain("triggers", after.ChangesSince(before).Keys);
	}

	[Fact]
	public void Options_ChangedRules_AreSentUnderTheirScriptName()
	{
		TerminalJsOptions before = TerminalJsOptions.From(new TerminalSettings(), Theme);
		IReadOnlyList<TerminalJsTrigger> rules = [new TerminalJsTrigger("x", false, null, true)];

		Dictionary<string, object?> changes = TerminalJsOptions.From(new TerminalSettings(), Theme, triggers: rules).ChangesSince(before);

		Assert.Same(rules, changes["triggers"]);
		Assert.Empty(before.Triggers);
		Assert.Equal(TerminalTriggerSettings.NotifyIntervalSeconds * 1000, before.TriggerNotifyMs);
	}

	[Theory]
	[InlineData(TriggerColor.Red, NoticeSeverity.Error)]
	[InlineData(TriggerColor.Orange, NoticeSeverity.Warning)]
	[InlineData(TriggerColor.Yellow, NoticeSeverity.Warning)]
	[InlineData(TriggerColor.Green, NoticeSeverity.Success)]
	[InlineData(TriggerColor.Blue, NoticeSeverity.Info)]
	[InlineData(TriggerColor.None, NoticeSeverity.Info)]
	public void Notice_TakesItsSeverityFromTheRulesColor(TriggerColor color, NoticeSeverity expected) =>
		Assert.Equal(expected, TriggerNotice.Severity(color));

	[Fact]
	public void Notice_NamesTheSession()
	{
		Assert.Equal("Trigger in root@web01", TriggerNotice.Title(" root@web01 "));
		Assert.Equal("Trigger", TriggerNotice.Title(null));
	}

	[Fact]
	public void Notice_CleansTheLineTheServerSent()
	{
		Assert.Equal("ERROR disk full on /var", TriggerNotice.Line("ERROR\tdisk  full\u202e on /var\r\n\u0007"));
		Assert.Equal("(an empty line)", TriggerNotice.Line(" \t\u0007 "));
		Assert.Equal("(an empty line)", TriggerNotice.Line(null));
	}

	[Fact]
	public void Notice_CutsALongLine()
	{
		string line = TriggerNotice.Line(new string('x', 1000));

		Assert.Equal(TriggerNotice.MaxLineLength + 3, line.Length);
		Assert.EndsWith("...", line, StringComparison.Ordinal);
	}

	[Fact]
	public void Notice_ForASlowRule_SaysWhereToFixIt() =>
		Assert.Contains("Triggers settings page", TriggerNotice.Slow(".*a.*b"), StringComparison.Ordinal);

	[Fact]
	public void Preview_LaterLayersWinWhereTheyOverlap_AndRangesPastTheEndAreIgnored()
	{
		IReadOnlyList<TriggerPreviewSegment> segments = TriggerPreviewSegment.Split(
			"build succeeded",
			[
				new TriggerLayer([new TriggerMatchRange(0, 5)], "#ef5350"),
				new TriggerLayer([new TriggerMatchRange(3, 3), new TriggerMatchRange(40, 5)], "#00e676"),
				new TriggerLayer([new TriggerMatchRange(0, 15)], null),
			]);

		Assert.Equal(
			[
				new TriggerPreviewSegment("bui", "#ef5350"),
				new TriggerPreviewSegment("ld ", "#00e676"),
				new TriggerPreviewSegment("succeeded", null),
			],
			segments);
	}

	[Fact]
	public void Preview_OfNothing_IsEmpty() => Assert.Empty(TriggerPreviewSegment.Split("", []));

	[Fact]
	public void Describe_SaysWhatARuleDoes()
	{
		Assert.Equal(
			"Colors it red, shows a notice, any case",
			TriggerSettingsPage.Describe(new TerminalTrigger { Pattern = "x", Highlight = TriggerColor.Red, Notify = true, IgnoreCase = true }));
		Assert.Equal("Shows a notice", TriggerSettingsPage.Describe(new TerminalTrigger { Pattern = "x", Notify = true }));
		Assert.Equal("Does nothing yet", TriggerSettingsPage.Describe(new TerminalTrigger { Pattern = "x" }));
	}

	[Fact]
	public void Outcome_SaysWhatARuleMakesOfTheSample()
	{
		TerminalTrigger rule = new() { Pattern = "x", Highlight = TriggerColor.Red, Notify = true };
		TriggerTestResult twice = new(true, null, [new TriggerMatchRange(0, 1), new TriggerMatchRange(2, 1)]);

		Assert.Equal("Matches 2 times and shows a notice", TriggerSettingsPage.Outcome(rule, twice));
		Assert.Equal("Matches, but the rule is off", TriggerSettingsPage.Outcome(rule with { Enabled = false }, twice));
		Assert.Equal("No match", TriggerSettingsPage.Outcome(rule, new TriggerTestResult(true, null, [])));
		Assert.Equal("Never runs: Unterminated group.", TriggerSettingsPage.Outcome(rule, new TriggerTestResult(false, "Unterminated group.", [])));
	}

	[Theory]
	[InlineData("", "Enter a pattern.")]
	[InlineData("ERROR", null)]
	public void LengthError_NeedsAPatternWithinTheCap(string pattern, string? expected) =>
		Assert.Equal(expected, TriggerEditorDialog.LengthError(pattern));

	[Fact]
	public void LengthError_PastTheCap_NamesIt() =>
		Assert.Equal(
			$"Keep it to {TerminalTriggerSettings.MaxPatternLength} characters.",
			TriggerEditorDialog.LengthError(new string('a', TerminalTriggerSettings.MaxPatternLength + 1)));

	[Fact]
	public void Switch_WithoutASession_IsKeptPerStream()
	{
		TerminalTriggerSwitch triggers = new();
		Fakes.RecordingTerminalStream first = new();
		Fakes.RecordingTerminalStream second = new();

		triggers.SetPaused(null, first, true);

		Assert.True(triggers.IsPaused(null, first));
		Assert.False(triggers.IsPaused(null, second));
		triggers.SetPaused(null, first, false);
		Assert.False(triggers.IsPaused(null, first));
	}

	[Fact]
	public void Switch_WithASession_FollowsTheSessionToANewStream()
	{
		// A tab moved to the other pane gets a new view and a new broadcast stream in front of the same session.
		TerminalTriggerSwitch triggers = new();
		Guid session = Guid.NewGuid();
		Fakes.RecordingTerminalStream before = new();
		Fakes.RecordingTerminalStream after = new();

		triggers.SetPaused(session, before, true);

		Assert.True(triggers.IsPaused(session, after));
		Assert.False(triggers.IsPaused(Guid.NewGuid(), before));
		triggers.SetPaused(session, after, false);
		Assert.False(triggers.IsPaused(session, before));
	}
}
