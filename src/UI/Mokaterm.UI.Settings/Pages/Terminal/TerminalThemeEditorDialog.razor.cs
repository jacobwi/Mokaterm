using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.UI.Settings.Pages.Terminal;

/// <summary>Edits a custom terminal theme (name, dark flag and every color) on a copy, with a live preview.</summary>
public sealed partial class TerminalThemeEditorDialog : ComponentBase
{
	private TerminalTheme? _draft;
	private string? _nameError;
	private bool _wasOpen;

	[Parameter]
	public bool Open { get; set; }

	[Parameter]
	public EventCallback<bool> OpenChanged { get; set; }

	/// <summary>The theme to edit. Changes stay on a copy until Save.</summary>
	[Parameter]
	public TerminalTheme? Theme { get; set; }

	/// <summary>True when <see cref="Theme"/> is a fresh duplicate that is not saved yet.</summary>
	[Parameter]
	public bool IsNew { get; set; }

	[Parameter]
	public string? FontFamily { get; set; }

	/// <summary>Receives the edited theme with a trimmed name.</summary>
	[Parameter]
	public EventCallback<TerminalTheme> OnSave { get; set; }

	protected override void OnParametersSet()
	{
		if (Open && !_wasOpen)
		{
			_draft = Theme;
			_nameError = null;
		}

		_wasOpen = Open;
	}

	private void SetName(string? name)
	{
		if (_draft is not null)
		{
			_draft = _draft with { Name = name ?? "" };
			_nameError = null;
		}
	}

	private void SetDark(bool isDark)
	{
		if (_draft is not null)
		{
			_draft = _draft with { IsDark = isDark };
		}
	}

	private void SetColor(ThemeColorField field, string value)
	{
		if (_draft is not null)
		{
			_draft = field.Set(_draft, value);
		}
	}

	private async Task SaveAsync()
	{
		if (_draft is null)
		{
			return;
		}

		string name = _draft.Name.Trim();
		if (name.Length == 0)
		{
			_nameError = "Give the theme a name.";
			return;
		}

		await OnSave.InvokeAsync(_draft with { Name = name });
		await OpenChanged.InvokeAsync(false);
	}

	private Task CancelAsync() => OpenChanged.InvokeAsync(false);
}
