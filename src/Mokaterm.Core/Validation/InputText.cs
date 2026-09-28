namespace Mokaterm.Core.Validation;

/// <summary>Normalizes free text coming from editors before it is stored.</summary>
internal static class InputText
{
	/// <summary>Trims <paramref name="value"/>; blank becomes null.</summary>
	public static string? TrimToNull(string? value)
	{
		string? trimmed = value?.Trim();
		return string.IsNullOrEmpty(trimmed) ? null : trimmed;
	}

	/// <summary>Trims <paramref name="value"/> and throws <see cref="ArgumentException"/> with <paramref name="message"/> when it is blank.</summary>
	public static string Require(string? value, string message, string paramName) =>
		TrimToNull(value) ?? throw new ArgumentException(message, paramName);
}
