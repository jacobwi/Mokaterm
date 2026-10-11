using System.Text;

namespace Mokaterm.Modules.Local.Shells;

/// <summary><c>%NAME%</c> expansion in paths a login names, the way the Command Prompt does it.</summary>
internal static class LocalVariables
{
	/// <summary>
	/// Replaces every <c>%NAME%</c> that has a value. One that has none stays as written, percent signs included, which
	/// is also what the Command Prompt does, so a folder that really has a percent sign in its name still works.
	/// </summary>
	public static string Expand(string text, Func<string, string?> lookup)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(lookup);
		if (!text.Contains('%', StringComparison.Ordinal))
		{
			return text;
		}

		StringBuilder expanded = new(text.Length);
		int index = 0;
		while (index < text.Length)
		{
			int start = text.IndexOf('%', index);
			int end = start < 0 ? -1 : text.IndexOf('%', start + 1);
			if (end < 0)
			{
				break;
			}

			string name = text[(start + 1)..end];
			if (name.Length > 0 && lookup(name) is { } value)
			{
				expanded.Append(text, index, start - index).Append(value);
				index = end + 1;
			}
			else
			{
				// Not a variable: the first percent sign is text, and the second may open the next name.
				expanded.Append(text, index, end - index);
				index = end;
			}
		}

		return expanded.Append(text, index, text.Length - index).ToString();
	}
}
