namespace Mokaterm.UI.Connections;

internal static class HostAddress
{
	/// <summary>Trims whitespace and the brackets people type around IPv6 addresses.</summary>
	public static string Normalize(string? address)
	{
		string trimmed = address?.Trim() ?? "";
		return trimmed.Length > 2 && trimmed[0] == '[' && trimmed[^1] == ']' ? trimmed[1..^1] : trimmed;
	}

	/// <summary>A message for the user when the address cannot be a hostname or IP address, otherwise null.</summary>
	public static string? Validate(string? address)
	{
		string normalized = Normalize(address);
		if (normalized.Length == 0)
		{
			return "Enter a hostname or IP address.";
		}

		return normalized.AsSpan().IndexOfAny(" /\\@") >= 0
			? "Enter only the hostname or IP address, without a user, path or spaces."
			: null;
	}
}
