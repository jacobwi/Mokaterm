using System.Security.Cryptography;

namespace MokatermPlugin1;

/// <summary>Random passwords from letters, digits and the settings' symbols, each character drawn on its own.</summary>
internal static class PasswordGenerator
{
	private const string LettersAndDigits = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

	/// <summary>Every character a password can hold, each once, so none is likelier than another.</summary>
	public static string Alphabet(PluginSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		return string.Concat((LettersAndDigits + settings.Symbols).Distinct());
	}

	public static string Generate(PluginSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		return RandomNumberGenerator.GetString(Alphabet(settings), settings.PasswordLength);
	}

	/// <summary>How many bits of randomness a password of these settings carries: what a guess has to get through.</summary>
	public static int StrengthBits(PluginSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		return (int)Math.Floor(settings.PasswordLength * Math.Log2(Alphabet(settings).Length));
	}
}
