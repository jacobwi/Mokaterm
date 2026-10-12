using Mokaterm.Abstractions.Settings;

namespace MokatermPlugin1;

/// <summary>
/// The plugin's settings, kept in Mokaterm's settings.json and carried by its settings backup. The key is
/// <c>plugin.</c> and the plugin's id (MokatermPluginId in the project file): the loader refuses a plugin whose
/// settings use any other name, so they can never land on one of Mokaterm's own sections.
/// </summary>
public sealed record PluginSettings : ISettingsSection
{
	public const int MinPasswordLength = 8;

	public const int MaxPasswordLength = 128;

	public const int MaxSymbolsLength = 32;

	public const string DefaultSymbols = "!#$%&*+-=?@^_";

	public static string SectionKey => "plugin.PLUGIN_ID";

	/// <summary>How many characters a generated password has.</summary>
	public int PasswordLength { get; init; } = 24;

	/// <summary>The punctuation a password draws from besides letters and digits. Empty means letters and digits only.</summary>
	public string Symbols { get; init; } = DefaultSymbols;

	/// <summary>A copy with every value inside what the settings page allows, since settings.json can be edited by hand.</summary>
	public PluginSettings Clamped() => this with
	{
		PasswordLength = Math.Clamp(PasswordLength, MinPasswordLength, MaxPasswordLength),
		Symbols = SymbolsProblem(Symbols) is null ? Symbols.Trim() : DefaultSymbols,
	};

	/// <summary>Why <paramref name="symbols"/> cannot be used, or null: printable ASCII other than letters, digits and spaces.</summary>
	public static string? SymbolsProblem(string? symbols)
	{
		if (symbols is null)
		{
			return "Symbols are missing.";
		}

		string trimmed = symbols.Trim();
		if (trimmed.Length > MaxSymbolsLength)
		{
			return $"Keep it to {MaxSymbolsLength} characters.";
		}

		return trimmed.All(c => c is > ' ' and <= '~' && !char.IsAsciiLetterOrDigit(c))
			? null
			: "Use punctuation such as ! # $ %, without letters, digits or spaces.";
	}
}
