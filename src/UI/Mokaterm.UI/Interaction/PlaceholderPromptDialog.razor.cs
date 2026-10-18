using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Commands;

namespace Mokaterm.UI.Interaction;

/// <summary>
/// One field per placeholder name, started from the prompt's values, with a live preview of the command they make.
/// A value with a control character is refused in its field, so Enter cannot send a line break into a terminal.
/// </summary>
public sealed partial class PlaceholderPromptDialog : PromptDialogBase
{
	private string[] _names = [];
	private string[] _values = [];

	[Parameter, EditorRequired]
	public PlaceholderPrompt Prompt { get; set; } = default!;

	/// <summary>Raised once with a value per name, or null when cancelled.</summary>
	[Parameter]
	public EventCallback<IReadOnlyDictionary<string, string>?> OnResult { get; set; }

	private bool HasProblem => _values.Any(value => CommandTemplate.ValueProblem(value) is not null);

	/// <summary>
	/// The command as it would go out now. A value the field refuses is shown without its control characters, which
	/// never reach the terminal anyway: the primary button stays off until the field is fixed.
	/// </summary>
	private string Preview => Prompt.Template.Fill(Collect(value => CommandTemplate.ValueProblem(value) is null
		? value
		: new string([.. value.Where(character => !char.IsControl(character))])));

	protected override void OnInitialized()
	{
		_names = [.. Prompt.Template.Placeholders.Select(placeholder => placeholder.Name)];
		_values =
		[
			.. Prompt.Template.Placeholders.Select(placeholder =>
				Prompt.Values.TryGetValue(placeholder.Name, out string? value) ? value : placeholder.Default ?? ""),
		];
	}

	private void SetValue(int index, string? value) => _values[index] = value ?? "";

	private Dictionary<string, string> Collect(Func<string, string> select)
	{
		Dictionary<string, string> values = new(StringComparer.Ordinal);
		for (int index = 0; index < _names.Length; index++)
		{
			values[_names[index]] = select(_values[index]);
		}

		return values;
	}

	private Task SubmitAsync()
	{
		// The button is off while a field refuses its value, and Enter presses only a button that is on; this is the
		// last word on it either way.
		if (HasProblem)
		{
			return Task.CompletedTask;
		}

		IReadOnlyDictionary<string, string> values = Collect(value => value);
		Array.Fill(_values, "");
		return AnswerAsync(OnResult, (IReadOnlyDictionary<string, string>?)values);
	}

	private Task CancelAsync()
	{
		Array.Fill(_values, "");
		return AnswerAsync(OnResult, (IReadOnlyDictionary<string, string>?)null);
	}
}
