using System.Text;

namespace Mokaterm.Modules.Local.Pty.Windows;

/// <summary>The environment block CreateProcessW takes with CREATE_UNICODE_ENVIRONMENT.</summary>
internal static class WindowsEnvironmentBlock
{
	/// <summary>
	/// <c>NAME=value</c> strings, each ending in a NUL and the whole block in one more, sorted by name without regard to
	/// case, which is the order Windows keeps its own blocks in. A name holding <c>=</c> or a NUL, and a value holding a
	/// NUL, cannot be written into a block at all and are left out.
	/// </summary>
	public static char[] Build(IReadOnlyDictionary<string, string> variables)
	{
		ArgumentNullException.ThrowIfNull(variables);
		StringBuilder block = new();
		foreach ((string name, string value) in variables.OrderBy(variable => variable.Key, StringComparer.OrdinalIgnoreCase))
		{
			if (name.Length == 0 || name.Contains('=', StringComparison.Ordinal) || name.Contains('\0', StringComparison.Ordinal)
				|| value.Contains('\0', StringComparison.Ordinal))
			{
				continue;
			}

			block.Append(name).Append('=').Append(value).Append('\0');
		}

		// An empty block still needs its two NULs: one ends the (missing) first string, the other ends the block.
		if (block.Length == 0)
		{
			block.Append('\0');
		}

		return block.Append('\0').ToString().ToCharArray();
	}
}
