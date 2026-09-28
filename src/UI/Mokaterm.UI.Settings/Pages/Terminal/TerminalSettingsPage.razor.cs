using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Settings.Pages.Terminal;

/// <summary>Settings page for <see cref="TerminalSettings"/>: theme picker, custom theme editor, font, cursor and input.</summary>
public sealed partial class TerminalSettingsPage : SettingsSectionBase<TerminalSettings>
{
	private const double LineHeightStep = 0.05;
	private const string SameAsDefaultChoice = "";

	private static readonly string DefaultThemeId = new TerminalSettings().ThemeId;

	private static readonly IReadOnlyList<SettingSuggestion> FontSuggestions =
	[
		new("JetBrains Mono", "'JetBrains Mono', monospace"),
		new("Cascadia Mono", "'Cascadia Mono', monospace"),
		new("Fira Code", "'Fira Code', monospace"),
		new("Consolas", "Consolas, monospace"),
		new("Menlo", "Menlo, monospace"),
		new("monospace", "monospace"),
	];

	private static readonly IReadOnlyList<SettingSuggestion> TerminalTypeSuggestions =
	[
		new("xterm-256color", "xterm-256color"),
		new("xterm", "xterm"),
		new("screen-256color", "screen-256color"),
		new("tmux-256color", "tmux-256color"),
		new("vt100", "vt100"),
	];

	private static readonly IReadOnlyList<EnumOption<TerminalCursorStyle>> CursorStyleOptions =
	[
		new(TerminalCursorStyle.Block, "Block"),
		new(TerminalCursorStyle.Underline, "Underline"),
		new(TerminalCursorStyle.Bar, "Bar"),
	];

	private static readonly IReadOnlyList<EnumOption<TerminalRightClickAction>> RightClickOptions =
	[
		new(TerminalRightClickAction.ContextMenu, "Menu"),
		new(TerminalRightClickAction.Paste, "Paste", MokaIcons.Content.Paste),
	];

	private static readonly IReadOnlyList<EnumOption<TerminalPromptColors>> PromptColorOptions =
	[
		new(TerminalPromptColors.Off, "Off"),
		new(TerminalPromptColors.WhenPlain, "Plain only"),
		new(TerminalPromptColors.Always, "Always"),
	];

	private static readonly IReadOnlyList<EnumOption<TerminalBellStyle>> BellOptions =
	[
		new(TerminalBellStyle.None, "Off"),
		new(TerminalBellStyle.Visual, "Flash"),
		new(TerminalBellStyle.Sound, "Sound", MokaIcons.Status.Bell),
	];

	private bool _importOpen;
	private bool _editorOpen;
	private bool _editorIsNew;
	private TerminalTheme? _editorTheme;

	[Inject]
	private ITerminalThemeCatalog ThemeCatalog { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private ITerminalThemeConverter Converter { get; set; } = default!;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	[Inject]
	private ILogger<TerminalSettingsPage> Logger { get; set; } = default!;

	/// <summary>Theme ids for the environment pickers, with an empty entry for "same as the default".</summary>
	private IReadOnlyList<string> ThemeChoices =>
		[SameAsDefaultChoice, .. ThemeCatalog.Themes.DistinctBy(theme => theme.Id).Select(theme => theme.Id)];

	private static string? ValidateFontFamily(string value) =>
		TerminalFontFamily.IsValid(value) ? null : TerminalFontFamily.Requirement;

	private static string? ValidateTerminalType(string value)
	{
		foreach (char c in value)
		{
			if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or '+'))
			{
				return "Use letters, digits, dots and dashes only.";
			}
		}

		return null;
	}

	private static IReadOnlyList<TerminalTheme> AddOrReplace(IReadOnlyList<TerminalTheme> themes, TerminalTheme theme) =>
		themes.Any(existing => existing.Id == theme.Id)
			? [.. themes.Select(existing => existing.Id == theme.Id ? theme : existing)]
			: [.. themes, theme];

	private static string ChoiceOf(string? themeId) => themeId ?? SameAsDefaultChoice;

	private static string? ThemeIdOf(string choice) => choice.Length == 0 ? null : choice;

	private string ThemeChoiceLabel(string choice) => choice.Length == 0 ? "Same as the default" : ThemeCatalog.Get(choice).Name;

	private Task SelectThemeAsync(string themeId) => SaveAsync(s => s with { ThemeId = themeId });

	private Task SetMinimumContrastAsync(double value) => SaveAsync(s => s with { MinimumContrastRatio = Math.Round(value, 1) });

	/// <summary>Adds an imported scheme as a custom theme and switches to it, since the user just chose it.</summary>
	private Task ImportThemeAsync(TerminalTheme theme) =>
		SaveAsync(s => s with { CustomThemes = AddOrReplace(s.CustomThemes, theme with { IsCustom = true }), ThemeId = theme.Id });

	private async Task CopyThemeAsync(TerminalTheme theme)
	{
		try
		{
			await Clipboard.WriteTextAsync(Converter.Export(theme, TerminalThemeFormat.WindowsTerminal));
		}
		catch (Exception ex)
		{
			// Browsers refuse clipboard writes from a page that is not focused or has no permission.
			Logger.LogWarning(ex, "Copying a terminal theme to the clipboard failed");
			Interaction.Notify(NoticeSeverity.Error, $"{theme.Name} could not be copied to the clipboard.");
			return;
		}

		Interaction.Notify(NoticeSeverity.Success, $"{theme.Name} is on the clipboard as a Windows Terminal scheme.", "Theme copied");
	}

	// Range inputs report binary fractions such as 1.1500000000000001; store the step value instead. The ranges
	// themselves belong to TerminalSettings, which SaveAsync applies, so these only round.
	private Task SetLineHeightAsync(double value) => SaveAsync(s => s with { LineHeight = Math.Round(value, 2) });

	private Task SetLetterSpacingAsync(double value) => SaveAsync(s => s with { LetterSpacing = Math.Round(value) });

	private Task ResetKeepingCustomThemesAsync() =>
		UpdateAsync(s => new TerminalSettings { CustomThemes = s.CustomThemes });

	private Task SaveAsync(Func<TerminalSettings, TerminalSettings> change) => UpdateAsync(s => change(s).Clamped());

	private void DuplicateTheme(TerminalTheme source)
	{
		_editorTheme = source with
		{
			Id = $"custom-{Guid.NewGuid()}",
			Name = $"{source.Name} copy",
			IsCustom = true,
		};
		_editorIsNew = true;
		_editorOpen = true;
	}

	private void EditTheme(TerminalTheme theme)
	{
		_editorTheme = theme;
		_editorIsNew = false;
		_editorOpen = true;
	}

	private Task SaveCustomThemeAsync(TerminalTheme theme)
	{
		TerminalTheme saved = theme with { IsCustom = true };
		bool selectSaved = _editorIsNew;
		return SaveAsync(s => s with
		{
			CustomThemes = AddOrReplace(s.CustomThemes, saved),
			ThemeId = selectSaved ? saved.Id : s.ThemeId,
		});
	}

	private async Task DeleteThemeAsync(TerminalTheme theme)
	{
		bool confirmed = (await Interaction.ConfirmAsync(new ConfirmPrompt
		{
			Title = "Delete custom theme",
			Message = $"Delete \"{theme.Name}\"? Terminals using it switch to the default theme.",
			ConfirmText = "Delete",
			Destructive = true,
		})).Confirmed;

		if (!confirmed)
		{
			return;
		}

		await SaveAsync(s => s with
		{
			CustomThemes = [.. s.CustomThemes.Where(custom => custom.Id != theme.Id)],
			ThemeId = s.ThemeId == theme.Id ? DefaultThemeId : s.ThemeId,
		});
	}
}
