using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Abstractions.Commands;

/// <summary>
/// A value a command asks for before it is typed or run, once however often its name appears. The default can be
/// anything the author wrote, a token included, so it prints as "(set)" only.
/// </summary>
public sealed record CommandPlaceholder(string Name, string? Default)
{
	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Name = ").Append(Name)
			.Append(", Default = ").Append(SecretText.Describe(Default));
		return true;
	}
}
