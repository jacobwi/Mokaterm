using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.QuickConnect;

/// <summary>The text the quick connect box holds for a target, written so that it reads back as the same parts.</summary>
internal static class QuickConnectText
{
	/// <summary>
	/// <c>user@host:port</c>, with an IPv6 address in brackets when a port follows it. Without a port the address stays
	/// bare, because the parser reads several colons without brackets as an IPv6 address with no port.
	/// </summary>
	public static string Format(string? username, string address, int? port)
	{
		ArgumentNullException.ThrowIfNull(address);
		string endpoint = port is { } value ? HostEndpoint.Format(address, value) : address;
		return string.IsNullOrWhiteSpace(username) ? endpoint : $"{username}@{endpoint}";
	}
}
