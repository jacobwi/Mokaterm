using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Terminal.Triggers;

namespace Mokaterm.UI.Settings.Pages.Triggers;

/// <summary>
/// Settings page for <see cref="TerminalTriggerSettings"/>: the rules in the order they run, and a sample line every
/// rule is tried on. Each rule is also run in the page whenever the list changes, so one that JavaScript would refuse
/// (a hand-edited file, a newer browser that reads a pattern differently) says so here instead of silently doing nothing.
/// </summary>
public sealed partial class TriggerSettingsPage : SettingsSectionBase<TerminalTriggerSettings>
{
	private string _sample = "";
	private bool _editorOpen;
	private TerminalTrigger? _editing;
	private IReadOnlyList<TerminalTrigger>? _testedRules;
	private string? _testedSample;
	private IReadOnlyList<TriggerTestResult>? _results;
	private bool _testing;

	[Inject]
	private TerminalTriggerTester Tester { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private ILogger<TriggerSettingsPage> Logger { get; set; } = default!;

	private static string RulesDescription => string.Create(
		CultureInfo.CurrentCulture,
		$"Each rule runs on every line a terminal prints, top to bottom, and where two color the same text the lower one wins. Up to {TerminalTriggerSettings.MaxRules} rules; a terminal's right-click menu pauses them for its session.");

	/// <summary>What a rule does, in a few words: <c>Colors it red, shows a notice, any case</c>.</summary>
	internal static string Describe(TerminalTrigger rule)
	{
		List<string> parts = [];
		if (rule.Highlight != TriggerColor.None)
		{
			parts.Add("colors it " + TriggerPalette.Name(rule.Highlight).ToLowerInvariant());
		}

		if (rule.Notify)
		{
			parts.Add("shows a notice");
		}

		if (parts.Count == 0)
		{
			parts.Add("does nothing yet");
		}

		if (rule.IgnoreCase)
		{
			parts.Add("any case");
		}

		string text = string.Join(", ", parts);
		return char.ToUpperInvariant(text[0]) + text[1..];
	}

	/// <summary>What one rule makes of the sample line.</summary>
	internal static string Outcome(TerminalTrigger rule, TriggerTestResult result)
	{
		if (!result.Valid)
		{
			return "Never runs: " + result.Error;
		}

		if (!result.IsMatch)
		{
			return rule.Enabled ? "No match" : "No match, and the rule is off";
		}

		if (!rule.Enabled)
		{
			return "Matches, but the rule is off";
		}

		string count = result.Matches.Count == 1
			? "Matches once"
			: string.Create(CultureInfo.CurrentCulture, $"Matches {result.Matches.Count} times");
		return rule.Notify ? count + " and shows a notice" : count;
	}

	protected override Task OnAfterRenderAsync(bool firstRender) => TestAsync();

	private static bool IsFirst(IReadOnlyList<TerminalTrigger> rules, TerminalTrigger rule) =>
		rules.Count > 0 && ReferenceEquals(rules[0], rule);

	private static bool IsLast(IReadOnlyList<TerminalTrigger> rules, TerminalTrigger rule) =>
		rules.Count > 0 && ReferenceEquals(rules[^1], rule);

	private static IReadOnlyList<TerminalTrigger> Replace(IReadOnlyList<TerminalTrigger> rules, Guid id, Func<TerminalTrigger, TerminalTrigger> change) =>
		[.. rules.Select(rule => rule.Id == id ? change(rule) : rule)];

	/// <summary>The layers of the sample's preview: what a terminal would paint, so rules that are off or broken paint nothing.</summary>
	private static IReadOnlyList<TriggerLayer> Layers(IReadOnlyList<TerminalTrigger> rules, IReadOnlyList<TriggerTestResult> results) =>
		[.. rules.Select((rule, index) => rule.Enabled && results[index].Valid
			? new TriggerLayer(results[index].Matches, TriggerPalette.Hex(rule.Highlight))
			: new TriggerLayer([], null))];

	private static string? ErrorFor(IReadOnlyList<TerminalTrigger> rules, IReadOnlyList<TriggerTestResult>? results, TerminalTrigger rule)
	{
		if (results is null)
		{
			return null;
		}

		for (int index = 0; index < rules.Count; index++)
		{
			if (ReferenceEquals(rules[index], rule))
			{
				return results[index].Valid ? null : results[index].Error;
			}
		}

		return null;
	}

	/// <summary>The results for these rules on the line now in the field, or null while they are still being worked out.</summary>
	private IReadOnlyList<TriggerTestResult>? ResultsFor(IReadOnlyList<TerminalTrigger> rules) =>
		ReferenceEquals(rules, _testedRules) && _sample == _testedSample && _results is { } results && results.Count == rules.Count
			? results
			: null;

	private void SetSample(string? value) => _sample = value ?? "";

	/// <summary>
	/// Runs every rule on the sample whenever either changed since the last run. One run at a time: changes that come in
	/// meanwhile are picked up by the loop, so typing never stacks calls into the page.
	/// </summary>
	private async Task TestAsync()
	{
		if (_testing)
		{
			return;
		}

		_testing = true;
		try
		{
			while (true)
			{
				IReadOnlyList<TerminalTrigger> rules = Settings.Rules;
				string sample = _sample;
				if (ReferenceEquals(rules, _testedRules) && sample == _testedSample)
				{
					return;
				}

				// Marked before the call, so a page that cannot answer is not asked again on every render.
				_testedRules = rules;
				_testedSample = sample;
				_results = rules.Count == 0 ? [] : await Tester.TestAsync(rules, sample);
				StateHasChanged();
			}
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			Logger.LogDebug(ex, "Testing the trigger rules failed.");
			_results = null;
		}
		finally
		{
			_testing = false;
		}
	}

	private void AddRule()
	{
		if (Settings.Rules.Count < TerminalTriggerSettings.MaxRules)
		{
			_editing = null;
			_editorOpen = true;
		}
	}

	private void EditRule(TerminalTrigger rule)
	{
		_editing = rule;
		_editorOpen = true;
	}

	private async Task SaveRuleAsync(TerminalTrigger rule)
	{
		IReadOnlyList<TerminalTrigger> rules = Settings.Rules;
		bool exists = rules.Any(existing => existing.Id == rule.Id);
		if (!exists && rules.Count >= TerminalTriggerSettings.MaxRules)
		{
			Interaction.Notify(
				NoticeSeverity.Warning,
				string.Create(CultureInfo.CurrentCulture, $"There are already {TerminalTriggerSettings.MaxRules} rules. Delete one to add another."));
			return;
		}

		await UpdateAsync(settings => settings with
		{
			// Matched by id rather than by position, so a list reordered in another window still gets the right rule.
			Rules = settings.Rules.Any(existing => existing.Id == rule.Id)
				? Replace(settings.Rules, rule.Id, _ => rule)
				: [.. settings.Rules, rule],
		});
	}

	private Task SetEnabledAsync(Guid id, bool enabled) =>
		UpdateAsync(settings => settings with { Rules = Replace(settings.Rules, id, rule => rule with { Enabled = enabled }) });

	private Task MoveAsync(Guid id, int offset) => UpdateAsync(settings =>
	{
		List<TerminalTrigger> rules = [.. settings.Rules];
		int from = rules.FindIndex(rule => rule.Id == id);
		int to = from + offset;
		if (from < 0 || to < 0 || to >= rules.Count)
		{
			return settings;
		}

		(rules[from], rules[to]) = (rules[to], rules[from]);
		return settings with { Rules = rules };
	});

	private async Task DeleteAsync(TerminalTrigger rule)
	{
		ConfirmResult answer = await Interaction.ConfirmAsync(new ConfirmPrompt
		{
			Title = "Delete trigger",
			Message = "Delete this rule? Terminals stop running it at once.",
			Details = rule.Pattern,
			ConfirmText = "Delete",
			Destructive = true,
		});

		if (answer.Confirmed)
		{
			await UpdateAsync(settings => settings with { Rules = [.. settings.Rules.Where(existing => existing.Id != rule.Id)] });
		}
	}
}
