using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Mokaterm.Modules.Ssh.Tunnels;

/// <summary>
/// One forwarded port saved with a connection, or added for a single session. Stored as one
/// <c>ssh.tunnel.NN</c> option per tunnel so a saved connection still loads after this record grows a field.
/// </summary>
public sealed record SshTunnelDefinition
{
	/// <summary>What a tunnel listens on when nothing else is asked for. Anything wider exposes the tunnel to the network.</summary>
	public const string DefaultListenHost = "127.0.0.1";

	public const int MaxNameLength = 60;

	public const int MaxHostLength = 255;

	/// <summary>Where a new local or remote tunnel starts, so it is complete enough to save straight away.</summary>
	public const int DefaultForwardPort = 8080;

	/// <summary>The port OpenSSH's own examples use for a SOCKS proxy.</summary>
	public const int DefaultSocksPort = 1080;

	private const char Separator = '|';

	private const int FieldCount = 8;

	/// <summary>Stable across edits, so a running tunnel keeps its row when the connection is saved again.</summary>
	public required Guid Id { get; init; }

	public SshTunnelKind Kind { get; init; }

	/// <summary>Shown instead of the endpoints. Optional.</summary>
	public string? Name { get; init; }

	/// <summary>Where the listening socket binds. Local and dynamic tunnels bind here, remote ones bind on the server.</summary>
	public string ListenHost { get; init; } = DefaultListenHost;

	/// <summary>The port to listen on. 0 lets the operating system pick a free one.</summary>
	public int ListenPort { get; init; }

	/// <summary>Where traffic comes out. Unused by <see cref="SshTunnelKind.Dynamic"/>, which resolves per request.</summary>
	public string DestinationHost { get; init; } = "";

	public int DestinationPort { get; init; }

	/// <summary>Opened right after the session connects, and again after a reconnect.</summary>
	public bool OpenWithSession { get; init; } = true;

	/// <summary>True when the kind sends traffic to one fixed address.</summary>
	public bool HasDestination => Kind != SshTunnelKind.Dynamic;

	/// <summary>The label for lists: the name when there is one, otherwise the endpoints.</summary>
	public string Display => string.IsNullOrWhiteSpace(Name) ? Describe() : Name;

	/// <summary>
	/// A new tunnel with the defaults the editor starts from. They are deliberately complete: an incomplete tunnel
	/// cannot be saved, so a half-filled row would vanish the moment the editor wrote it back.
	/// </summary>
	public static SshTunnelDefinition Create(SshTunnelKind kind) => new()
	{
		Id = Guid.NewGuid(),
		Kind = kind,
		ListenHost = DefaultListenHost,
		ListenPort = kind == SshTunnelKind.Dynamic ? DefaultSocksPort : DefaultForwardPort,
		DestinationHost = kind == SshTunnelKind.Dynamic ? "" : "localhost",
		DestinationPort = kind == SshTunnelKind.Dynamic ? 0 : DefaultForwardPort,
	};

	/// <summary>A host name or address that can go in an option value and in an SSH channel request.</summary>
	public static bool IsValidHost([NotNullWhen(true)] string? host) =>
		!string.IsNullOrWhiteSpace(host)
		&& host.Length <= MaxHostLength
		&& host.IndexOf(Separator, StringComparison.Ordinal) < 0
		&& !host.Any(char.IsWhiteSpace)
		&& !host.Contains('\0', StringComparison.Ordinal);

	public static bool IsValidPort(int port, bool allowZero) => allowZero ? port is >= 0 and <= 65535 : port is >= 1 and <= 65535;

	/// <summary>Reads one stored value. Null when the value is not a tunnel this version understands.</summary>
	public static SshTunnelDefinition? Parse(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		// The name is last and unsplit, so a name may contain the separator.
		string[] fields = value.Split(Separator, FieldCount);
		if (fields.Length < FieldCount - 1
			|| !Guid.TryParse(fields[0], CultureInfo.InvariantCulture, out Guid id)
			|| !Enum.TryParse(fields[1], ignoreCase: true, out SshTunnelKind kind)
			|| !Enum.IsDefined(kind)
			|| !TryParsePort(fields[3], out int listenPort)
			|| !TryParsePort(fields[5], out int destinationPort))
		{
			return null;
		}

		SshTunnelDefinition tunnel = new()
		{
			Id = id,
			Kind = kind,
			ListenHost = IsValidHost(fields[2]) ? fields[2] : DefaultListenHost,
			ListenPort = listenPort,
			DestinationHost = IsValidHost(fields[4]) ? fields[4] : "",
			DestinationPort = destinationPort,
			OpenWithSession = !string.Equals(fields[6], "manual", StringComparison.OrdinalIgnoreCase),
			Name = fields.Length == FieldCount ? NullIfBlank(Trim(fields[7])) : null,
		};

		return tunnel.Validate() is null ? tunnel : null;
	}

	/// <summary>The stored form. <see cref="Parse"/> reads it back.</summary>
	public string Format() => string.Join(
		Separator,
		Id.ToString("D", CultureInfo.InvariantCulture),
		Kind.ToString(),
		ListenHost,
		ListenPort.ToString(CultureInfo.InvariantCulture),
		HasDestination ? DestinationHost : "",
		HasDestination ? DestinationPort.ToString(CultureInfo.InvariantCulture) : "0",
		OpenWithSession ? "auto" : "manual",
		Trim(Name ?? ""));

	/// <summary>Null when the tunnel can be opened, otherwise why it cannot.</summary>
	public string? Validate()
	{
		if (!IsValidHost(ListenHost))
		{
			return "The listen address is not a host name or address.";
		}

		if (!IsValidPort(ListenPort, allowZero: Kind != SshTunnelKind.Remote))
		{
			return Kind == SshTunnelKind.Remote
				? "The server needs a port from 1 to 65535 to listen on."
				: "The listen port must be from 0 to 65535. 0 picks a free port.";
		}

		if (!HasDestination)
		{
			return null;
		}

		if (!IsValidHost(DestinationHost))
		{
			return "The destination is not a host name or address.";
		}

		return IsValidPort(DestinationPort, allowZero: false) ? null : "The destination port must be from 1 to 65535.";
	}

	/// <summary>The endpoints in the shape <c>ssh -L</c> writes them.</summary>
	public string Describe() => Kind switch
	{
		SshTunnelKind.Dynamic => string.Create(CultureInfo.InvariantCulture, $"SOCKS on {ListenHost}:{PortText(ListenPort)}"),
		SshTunnelKind.Remote => string.Create(CultureInfo.InvariantCulture, $"{ListenHost}:{PortText(ListenPort)} on the server to {DestinationHost}:{DestinationPort}"),
		_ => string.Create(CultureInfo.InvariantCulture, $"{ListenHost}:{PortText(ListenPort)} to {DestinationHost}:{DestinationPort}"),
	};

	private static string PortText(int port) => port == 0 ? "auto" : port.ToString(CultureInfo.InvariantCulture);

	private static string Trim(string value) => value.Length <= MaxNameLength ? value : value[..MaxNameLength];

	private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

	private static bool TryParsePort(string value, out int port) =>
		int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 0 and <= 65535;
}
