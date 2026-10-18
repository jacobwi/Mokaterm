using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Abstractions.Commands;

/// <summary>
/// One piece of a <see cref="CommandTemplate"/>: literal text, or a placeholder when <see cref="Name"/> is set. A
/// command can carry a token, so the text and the default print as "(set)" only.
/// </summary>
public sealed record CommandTemplatePart
{
	/// <summary>Literal text as it will be typed, or the placeholder as it was written, such as <c>{{port:22}}</c>.</summary>
	public required string Text { get; init; }

	/// <summary>The placeholder's name, or null for literal text.</summary>
	public string? Name { get; init; }

	/// <summary>The default written at this place, or null when none was.</summary>
	public string? Default { get; init; }

	public bool IsPlaceholder => Name is not null;

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Text = ").Append(SecretText.Describe(Text))
			.Append(", Name = ").Append(Name ?? "null")
			.Append(", Default = ").Append(SecretText.Describe(Default));
		return true;
	}
}
