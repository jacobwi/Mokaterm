using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class TerminalTriggerSettingsTests
{
	[Fact]
	public void Clamped_WithinLimits_ReturnsTheSameSection()
	{
		TerminalTriggerSettings settings = new() { Rules = [Rule("ERROR|FATAL"), Rule("build succeeded")] };

		// Handing the same section back is what lets an update that changed nothing be recognized as one.
		Assert.Same(settings, settings.Clamped());
	}

	[Fact]
	public void Clamped_PastTheRuleCap_DropsTheRulesAfterIt()
	{
		TerminalTrigger[] rules = [.. Enumerable.Range(0, TerminalTriggerSettings.MaxRules + 5).Select(index => Rule($"rule{index}"))];

		TerminalTriggerSettings clamped = new TerminalTriggerSettings { Rules = rules }.Clamped();

		Assert.Equal(TerminalTriggerSettings.MaxRules, clamped.Rules.Count);
		Assert.Equal(rules.Take(TerminalTriggerSettings.MaxRules), clamped.Rules);
	}

	[Fact]
	public void Clamped_DropsPatternsPastTheLengthCapAndEmptyOnes_RatherThanCuttingThem()
	{
		TerminalTrigger kept = Rule(new string('a', TerminalTriggerSettings.MaxPatternLength));
		TerminalTriggerSettings settings = new()
		{
			Rules = [Rule(new string('a', TerminalTriggerSettings.MaxPatternLength + 1)), kept, Rule(""), null!],
		};

		Assert.Equal([kept], settings.Clamped().Rules);
	}

	[Fact]
	public void Clamped_ADroppedRule_LeavesRoomForTheNext()
	{
		TerminalTrigger[] rules =
		[
			Rule(""),
			.. Enumerable.Range(0, TerminalTriggerSettings.MaxRules).Select(index => Rule($"rule{index}")),
		];

		Assert.Equal(rules.Skip(1), new TerminalTriggerSettings { Rules = rules }.Clamped().Rules);
	}

	[Fact]
	public void Clamped_WithoutAList_HasAnEmptyOne() =>
		Assert.Empty(new TerminalTriggerSettings { Rules = null! }.Clamped().Rules);

	[Fact]
	public void Active_LeavesOutRulesThatAreOffOrDoNothing()
	{
		TerminalTrigger colors = Rule("ERROR");
		TerminalTrigger notifies = Rule("done") with { Highlight = TriggerColor.None, Notify = true };
		TerminalTriggerSettings settings = new()
		{
			Rules = [colors, Rule("off") with { Enabled = false }, Rule("idle") with { Highlight = TriggerColor.None }, notifies],
		};

		Assert.Equal([colors, notifies], settings.Active);
	}

	[Theory]
	[InlineData("", false)]
	[InlineData(null, false)]
	[InlineData("x", true)]
	public void IsWithinLimits_NeedsSomethingToMatch(string? pattern, bool expected) =>
		Assert.Equal(expected, TerminalTriggerSettings.IsWithinLimits(pattern));

	private static TerminalTrigger Rule(string pattern) =>
		new() { Id = Guid.NewGuid(), Pattern = pattern, Highlight = TriggerColor.Red };
}
