using System.Text.RegularExpressions;

namespace Mokaterm.Core.Storage;

/// <summary>Validates document names shared by the plain and encrypted stores.</summary>
internal static partial class DocumentNames
{
	/// <summary>The plain store refuses this name because the vault header lives at <c>vault.json</c>.</summary>
	public const string ReservedVaultHeader = "vault";

	public static void Validate(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		if (!NamePattern().IsMatch(name))
		{
			throw new ArgumentException($"'{name}' is not a valid document name. Use 1 to 64 lowercase letters, digits, dots and dashes, starting with a letter or digit.", nameof(name));
		}
	}

	// \z instead of $: in .NET, $ also matches before a trailing newline, which would let "settings\n" through.
	[GeneratedRegex(@"^[a-z0-9][a-z0-9.-]{0,63}\z", RegexOptions.CultureInvariant)]
	private static partial Regex NamePattern();
}
