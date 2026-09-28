using System.Globalization;

namespace Mokaterm.Abstractions.Security;

/// <summary>The single rule for master passwords, enforced by the vault and shown by every screen that sets one.</summary>
public static class VaultPasswordPolicy
{
	public const int MinimumLength = 8;

	/// <summary>The rule as a short hint, for example under a password field.</summary>
	public static string Requirement { get; } = string.Create(CultureInfo.CurrentCulture, $"At least {MinimumLength} characters.");

	public static bool IsAcceptable(string? password) => password is { Length: >= MinimumLength };
}
