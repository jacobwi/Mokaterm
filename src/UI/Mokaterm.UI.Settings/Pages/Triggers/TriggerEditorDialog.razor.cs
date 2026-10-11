using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Terminal.Triggers;

namespace Mokaterm.UI.Settings.Pages.Triggers;

/// <summary>
/// Edits one trigger on a draft. The pattern is checked in the page as it is typed, by the script the terminals run it
/// with, and a rule is only handed back once that check passed: what JavaScript refuses here is exactly what a terminal
/// would skip.
/// </summary>
public sealed partial class TriggerEditorDialog : DraftDialogBase
{
	private static readonly IReadOnlyList<EnumOption<TriggerColor>> ColorOptions =
	[
		new(TriggerColor.None, TriggerPalette.Name(TriggerColor.None)),
		.. TriggerPalette.Colors.Select(color => new EnumOption<TriggerColor>(color, TriggerPalette.Name(color))),
	];

	private string _pattern = "";
	private bool _ignoreCase;
	private TriggerColor _highlight;
	private bool _notify;
	private string _sample = "";
	private string? _patternError;
	private string? _error;
	private TriggerTestResult? _result;
	private bool _saving;
	private int _checks;

	[Inject]
	private TerminalTriggerTester Tester { get; set; } = default!;

	[Inject]
	private ILogger<TriggerEditorDialog> Logger { get; set; } = default!;

	/// <summary>The rule to edit, or null for a new one.</summary>
	[Parameter]
	public TerminalTrigger? Rule { get; set; }

	/// <summary>Receives the rule once its pattern passed the check. Saving it is the caller's.</summary>
	[Parameter]
	public EventCallback<TerminalTrigger> OnSave { get; set; }

	protected override bool IsBusy => _saving;

	private bool IsNew => Rule is null;

	private static string NotifyHelp => string.Create(
		CultureInfo.CurrentCulture,
		$"A notice names the session and shows the line, at most once every {TerminalTriggerSettings.NotifyIntervalSeconds} seconds for this rule.");

	/// <summary>The message for a pattern that is empty or over the length cap, which needs no trip to the page.</summary>
	internal static string? LengthError(string pattern) => pattern.Length switch
	{
		0 => "Enter a pattern.",
		> TerminalTriggerSettings.MaxPatternLength => string.Create(
			CultureInfo.CurrentCulture,
			$"Keep it to {TerminalTriggerSettings.MaxPatternLength} characters."),
		_ => null,
	};

	protected override void Load()
	{
		_pattern = Rule?.Pattern ?? "";
		_ignoreCase = Rule?.IgnoreCase ?? false;
		_highlight = Rule?.Highlight ?? TriggerColor.Red;
		_notify = Rule?.Notify ?? false;
	}

	protected override void Clear()
	{
		_pattern = "";
		_ignoreCase = false;
		_highlight = TriggerColor.None;
		_notify = false;
		_sample = "";
		_patternError = null;
		_error = null;
		_result = null;
		_saving = false;
		_checks++;
	}

	private async Task SetPatternAsync(string? value)
	{
		_pattern = value ?? "";
		_ = await CheckAsync();
	}

	private async Task SetIgnoreCaseAsync(bool value)
	{
		_ignoreCase = value;
		_ = await CheckAsync();
	}

	private void SetHighlight(TriggerColor color) => _highlight = color;

	private void SetNotify(bool notify)
	{
		_notify = notify;
		_error = null;
	}

	private async Task SetSampleAsync(string? value)
	{
		_sample = value ?? "";
		_ = await CheckAsync();
	}

	private IReadOnlyList<TriggerLayer> PreviewLayers(TriggerTestResult result) =>
		[new TriggerLayer(result.Matches, TriggerPalette.Hex(_highlight))];

	private static string MatchSummary(TriggerTestResult result) => result.Matches.Count == 1
		? "One match on this line."
		: string.Create(CultureInfo.CurrentCulture, $"{result.Matches.Count} matches on this line.");

	private TerminalTrigger Draft() => (Rule ?? new TerminalTrigger { Id = Guid.NewGuid() }) with
	{
		Pattern = _pattern,
		IgnoreCase = _ignoreCase,
		Highlight = _highlight,
		Notify = _notify,
	};

	/// <summary>
	/// Runs the draft in the page. Typing calls this on every keystroke, so a result only lands when no newer check
	/// started while it ran. Null when the page could not be asked or a newer check took over.
	/// </summary>
	private async Task<TriggerTestResult?> CheckAsync()
	{
		int check = ++_checks;
		_patternError = LengthError(_pattern);
		if (_patternError is not null)
		{
			_result = null;
			return null;
		}

		TriggerTestResult? result;
		try
		{
			IReadOnlyList<TriggerTestResult> results = await Tester.TestAsync([Draft()], _sample);
			result = results.Count > 0 ? results[0] : null;
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			Logger.LogDebug(ex, "Checking a trigger pattern failed.");
			return null;
		}

		if (check != _checks)
		{
			return null;
		}

		_result = result;
		_patternError = result is { Valid: false } ? result.Error : null;
		return result;
	}

	private async Task SaveAsync()
	{
		if (_saving)
		{
			return;
		}

		_error = null;
		if (_highlight == TriggerColor.None && !_notify)
		{
			_error = "Pick a color, a notice or both. A rule that does neither never runs.";
			return;
		}

		_saving = true;
		try
		{
			TriggerTestResult? result = await CheckAsync();
			if (result is not { Valid: true })
			{
				_patternError ??= "The pattern could not be checked here, so it was not saved.";
				return;
			}

			TerminalTrigger rule = Draft();
			_saving = false;
			await CloseAsync();
			await OnSave.InvokeAsync(rule);
		}
		finally
		{
			_saving = false;
		}
	}
}
