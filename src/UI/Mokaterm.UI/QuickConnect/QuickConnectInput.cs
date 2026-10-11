using System.Globalization;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.QuickConnect;

/// <summary>
/// The one reading of the quick connect box: which protocol its text asks for, whether the rest is a target that
/// protocol can open, and the unsaved host and login that open it.
/// </summary>
internal static class QuickConnectInput
{
	/// <summary>What a protocol that names no example of its own takes.</summary>
	public const string DefaultExample = "user@host:port";

	/// <summary>The hint for a port that is a number but not one a port can be, which is the commonest slip.</summary>
	public const string PortRange = "A port is a number from 1 to 65535.";

	private const string SchemeSeparator = "://";

	/// <summary>
	/// The protocol the text asks for: the one its <c>scheme://</c> names when a module registered it, otherwise
	/// <paramref name="pickedProtocolId"/>. A scheme nothing registered leaves the picked one, so the picker never
	/// lands on a protocol it does not list.
	/// </summary>
	public static string ProtocolFor(string? text, string pickedProtocolId, IProtocolRegistry protocols)
	{
		ArgumentNullException.ThrowIfNull(protocols);
		return QuickConnectTarget.ReadScheme(text) is { } scheme && protocols.Find(scheme) is { } provider
			? provider.Descriptor.Id
			: pickedProtocolId;
	}

	/// <summary>The placeholder for <paramref name="protocol"/>.</summary>
	public static string Example(ProtocolDescriptor? protocol) =>
		protocol?.QuickConnectExample is { Length: > 0 } example ? example : DefaultExample;

	/// <summary>The hint for text <paramref name="protocol"/> could not read.</summary>
	public static string Expected(ProtocolDescriptor protocol)
	{
		ArgumentNullException.ThrowIfNull(protocol);
		return $"For {protocol.DisplayName}, type {Example(protocol)}.";
	}

	/// <summary>
	/// Reads <paramref name="text"/> for the protocol it asks for. A protocol with a network port reads
	/// <c>user@host:port</c>; one without takes the whole text as the address and lets its module check it, which is
	/// how <c>/dev/ttyUSB0</c> reaches a serial port.
	/// </summary>
	public static QuickConnectResolution Resolve(string? text, string pickedProtocolId, IProtocolRegistry protocols)
	{
		ArgumentNullException.ThrowIfNull(protocols);
		string trimmed = text?.Trim() ?? "";
		string? scheme = QuickConnectTarget.ReadScheme(trimmed);
		if (protocols.Find(scheme ?? pickedProtocolId) is not { } provider)
		{
			return QuickConnectResolution.Invalid(scheme is null
				? "No installed module can open a connection."
				: $"No installed module opens {scheme}:// addresses.");
		}

		ProtocolDescriptor protocol = provider.Descriptor;
		bool read = protocol.UsesPort
			? QuickConnectTarget.TryParse(trimmed, out QuickConnectTarget? target)
			: QuickConnectTarget.TryParseName(trimmed, out target);
		if (!read || target is null)
		{
			return QuickConnectResolution.Invalid(
				protocol.UsesPort && HasPortOutOfRange(scheme is null ? trimmed : trimmed[(trimmed.IndexOf(SchemeSeparator, StringComparison.Ordinal) + SchemeSeparator.Length)..])
					? PortRange
					: Expected(protocol),
				protocol);
		}

		if (provider is IQuickConnectSuggestions addresses && addresses.ValidateQuickConnectAddress(target.Address) is { } problem)
		{
			return QuickConnectResolution.Invalid(problem, protocol);
		}

		return QuickConnectResolution.Valid(protocol, target);
	}

	/// <summary>
	/// True when <paramref name="target"/> ends in a port that is all digits but out of range. Only a host with no colon
	/// of its own, or a bracketed one, has a port there: a bare IPv6 address ends in digits after a colon too.
	/// </summary>
	private static bool HasPortOutOfRange(string target)
	{
		int colon = target.LastIndexOf(':');
		if (colon < 0 || colon == target.Length - 1)
		{
			return false;
		}

		ReadOnlySpan<char> port = target.AsSpan(colon + 1);
		foreach (char c in port)
		{
			if (!char.IsAsciiDigit(c))
			{
				return false;
			}
		}

		ReadOnlySpan<char> host = target.AsSpan(0, colon);
		host = host[(host.LastIndexOf('@') + 1)..];
		bool hasPort = host.EndsWith("]", StringComparison.Ordinal) || !host.Contains(':');
		return hasPort && (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value is < 1 or > 65535);
	}

	/// <summary>The unsaved host and login a quick connect opens, or the connection editor starts from.</summary>
	public static (HostProfile Host, ConnectionProfile Connection) CreateProfiles(
		ProtocolDescriptor protocol,
		QuickConnectTarget target,
		DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(protocol);
		ArgumentNullException.ThrowIfNull(target);
		HostProfile host = new()
		{
			Id = Guid.NewGuid(),
			Address = target.Address,
			CreatedAt = now,
			UpdatedAt = now,
		};

		ConnectionProfile connection = new()
		{
			Id = Guid.NewGuid(),
			HostId = host.Id,
			ProtocolId = protocol.Id,
			Port = protocol.UsesPort ? target.Port : null,
			Username = string.IsNullOrWhiteSpace(target.Username) ? null : target.Username,
			AuthenticationMethod = protocol.AuthenticationMethods.Count > 0 ? protocol.AuthenticationMethods[0] : AuthenticationMethod.Password,
			CreatedAt = now,
			UpdatedAt = now,
		};

		return (host, connection);
	}
}
