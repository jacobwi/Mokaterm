using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Common.Components;

/// <summary>A free-text setting with optional one-click suggestions. Text is trimmed; blank or rejected text is never saved.</summary>
public sealed partial class TextSetting : DraftSettingBase<string>
{
	[Parameter]
	public IReadOnlyList<SettingSuggestion>? Suggestions { get; set; }

	/// <summary>Returns a message for trimmed text that must not be saved, or null when it is fine.</summary>
	[Parameter]
	public Func<string, string?>? Validate { get; set; }

	/// <summary>Saves an empty string when the field is cleared instead of showing <see cref="EmptyError"/>.</summary>
	[Parameter]
	public bool AllowEmpty { get; set; }

	[Parameter]
	public string EmptyError { get; set; } = "This can't be empty.";

	protected override bool TryParse(string text, [MaybeNullWhen(false)] out string value, [NotNullWhen(false)] out string? error)
	{
		string trimmed = text.Trim();
		error = trimmed.Length == 0
			? AllowEmpty ? null : EmptyError
			: Validate?.Invoke(trimmed);

		if (error is not null)
		{
			value = null;
			return false;
		}

		value = trimmed;
		return true;
	}

	protected override string Format(string value) => value;
}
