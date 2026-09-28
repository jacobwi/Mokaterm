using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Tunnels;

namespace Mokaterm.Modules.Ssh;

/// <summary>Whether a connection may run file operations as root through sudo.</summary>
public enum SshElevationMode
{
	/// <summary>Offer "run as root" and ask for the sudo password when sudo needs one.</summary>
	Auto,

	/// <summary>Never run anything as root.</summary>
	None,
}

/// <summary>
/// Typed view of the <c>ssh.*</c> keys in <see cref="ProtocolOptions"/>, shared by the ssh and sftp protocols.
/// Unset values fall back to <see cref="SshSettings"/> and the terminal settings.
/// </summary>
public sealed record SshConnectionOptions
{
	public const string KeepAliveSecondsKey = "ssh.keepAliveSeconds";

	public const string StartupCommandKey = "ssh.startupCommand";

	public const string TerminalTypeKey = "ssh.terminalType";

	public const string InitialDirectoryKey = "ssh.initialDirectory";

	public const string ElevationKey = "ssh.elevation";

	/// <summary>Comma-separated connection ids, in the order the session hops through them.</summary>
	public const string JumpHostsKey = "ssh.jumpHosts";

	/// <summary>The fingerprint of the one agent key this login uses. Unset offers every key the agent holds.</summary>
	public const string AgentIdentityKey = "ssh.agentIdentity";

	/// <summary>One key per tunnel: <c>ssh.tunnel.00</c>, <c>ssh.tunnel.01</c> and so on.</summary>
	public const string TunnelKeyPrefix = "ssh.tunnel.";

	/// <summary>The proxy group: <c>ssh.proxy.kind</c>, <c>.host</c>, <c>.port</c> and <c>.user</c>.</summary>
	public const string ProxyKeyPrefix = "ssh.proxy.";

	public const int MaxTunnels = 64;

	/// <summary>How many hops one connection may name. A hop may have hops of its own, which the connector caps again.</summary>
	public const int MaxJumpHosts = 8;

	private const int MaxTerminalTypeLength = 64;

	/// <summary>Overrides <see cref="SshSettings.KeepAliveSeconds"/>. 0 turns keepalives off for this connection.</summary>
	public int? KeepAliveSeconds { get; init; }

	/// <summary>Typed into the shell once, right after it opens.</summary>
	public string? StartupCommand { get; init; }

	/// <summary>TERM for the pseudo-terminal, overriding the terminal settings.</summary>
	public string? TerminalType { get; init; }

	/// <summary>Where the file browser starts instead of the home directory. May start with <c>~</c>.</summary>
	public string? InitialDirectory { get; init; }

	public SshElevationMode Elevation { get; init; }

	/// <summary>Saved logins this connection hops through, first hop first. Empty dials the target directly.</summary>
	public IReadOnlyList<Guid> JumpConnectionIds { get; init; } = [];

	/// <summary>Which key of the agent to use, as <c>SHA256:...</c>. Null uses whichever the server accepts.</summary>
	public string? AgentIdentity { get; init; }

	/// <summary>Forwarded ports saved with this connection.</summary>
	public IReadOnlyList<SshTunnelDefinition> Tunnels { get; init; } = [];

	/// <summary>
	/// The proxy this login dials through. It is used only for a socket that leaves this machine: behind jump hosts the
	/// socket goes to a loopback port an earlier hop holds open, and the first hop's own proxy carries the chain.
	/// </summary>
	public ProxyOptions Proxy { get; init; } = ProxyOptions.None;

	public static SshConnectionOptions FromOptions(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return new SshConnectionOptions
		{
			KeepAliveSeconds = ParseSeconds(options.GetString(KeepAliveSecondsKey)),
			StartupCommand = NullIfBlank(options.GetString(StartupCommandKey)),
			TerminalType = NullIfBlank(options.GetString(TerminalTypeKey)),
			InitialDirectory = NullIfBlank(options.GetString(InitialDirectoryKey)),
			Elevation = ParseElevation(options.GetString(ElevationKey)),
			JumpConnectionIds = ParseJumpHosts(options.GetString(JumpHostsKey)),
			AgentIdentity = NullIfBlank(options.GetString(AgentIdentityKey)),
			Tunnels = ParseTunnels(options),
			Proxy = ProxyOptions.From(options, ProxyKeyPrefix),
		};
	}

	/// <summary>Writes these values into <paramref name="options"/>. Unset values remove their key; other keys stay.</summary>
	public ProtocolOptions ApplyTo(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		ProtocolOptions written = options
			.With(KeepAliveSecondsKey, KeepAliveSeconds is >= 0 ? KeepAliveSeconds : null)
			.With(StartupCommandKey, NullIfBlank(StartupCommand))
			.With(TerminalTypeKey, NullIfBlank(TerminalType))
			.With(InitialDirectoryKey, NullIfBlank(InitialDirectory))
			.With(ElevationKey, Elevation == SshElevationMode.Auto ? null : Elevation.ToString())
			.With(AgentIdentityKey, NullIfBlank(AgentIdentity))
			.With(JumpHostsKey, FormatJumpHosts(JumpConnectionIds));

		written = Proxy.ApplyTo(written, ProxyKeyPrefix);

		// Every tunnel key is rewritten, so removing one leaves no gap behind.
		foreach (string key in written.Keys.Where(IsTunnelKey).ToArray())
		{
			written = written.With(key, (string?)null);
		}

		int index = 0;
		foreach (SshTunnelDefinition tunnel in Tunnels.Take(MaxTunnels))
		{
			if (tunnel.Validate() is null)
			{
				written = written.With(TunnelKey(index), tunnel.Format());
				index++;
			}
		}

		return written;
	}

	/// <summary>True for TERM values servers accept, such as <c>xterm-256color</c>.</summary>
	public static bool IsValidTerminalType([NotNullWhen(true)] string? terminalType) =>
		!string.IsNullOrEmpty(terminalType)
		&& terminalType.Length <= MaxTerminalTypeLength
		&& terminalType.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '+');

	public bool Equals(SshConnectionOptions? other) =>
		other is not null
		&& KeepAliveSeconds == other.KeepAliveSeconds
		&& StartupCommand == other.StartupCommand
		&& TerminalType == other.TerminalType
		&& InitialDirectory == other.InitialDirectory
		&& Elevation == other.Elevation
		&& AgentIdentity == other.AgentIdentity
		&& Proxy == other.Proxy
		&& JumpConnectionIds.SequenceEqual(other.JumpConnectionIds)
		&& Tunnels.SequenceEqual(other.Tunnels);

	public override int GetHashCode()
	{
		HashCode hash = default;
		hash.Add(KeepAliveSeconds);
		hash.Add(StartupCommand);
		hash.Add(TerminalType);
		hash.Add(InitialDirectory);
		hash.Add(Elevation);
		hash.Add(AgentIdentity);
		hash.Add(Proxy);
		foreach (Guid jumpHost in JumpConnectionIds)
		{
			hash.Add(jumpHost);
		}

		foreach (SshTunnelDefinition tunnel in Tunnels)
		{
			hash.Add(tunnel);
		}

		return hash.ToHashCode();
	}

	private static string TunnelKey(int index) =>
		TunnelKeyPrefix + index.ToString("D2", CultureInfo.InvariantCulture);

	private static bool IsTunnelKey(string key) => key.StartsWith(TunnelKeyPrefix, StringComparison.Ordinal);

	private static List<SshTunnelDefinition> ParseTunnels(ProtocolOptions options)
	{
		List<SshTunnelDefinition> tunnels = [];
		foreach (KeyValuePair<string, string> entry in options.Where(entry => IsTunnelKey(entry.Key)).OrderBy(entry => entry.Key, StringComparer.Ordinal))
		{
			if (tunnels.Count >= MaxTunnels)
			{
				break;
			}

			// A value this version cannot read is dropped rather than shown as a broken row.
			if (SshTunnelDefinition.Parse(entry.Value) is { } tunnel && !tunnels.Exists(existing => existing.Id == tunnel.Id))
			{
				tunnels.Add(tunnel);
			}
		}

		return tunnels;
	}

	private static List<Guid> ParseJumpHosts(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return [];
		}

		List<Guid> hosts = [];
		foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (hosts.Count >= MaxJumpHosts)
			{
				break;
			}

			if (Guid.TryParse(part, CultureInfo.InvariantCulture, out Guid id) && id != Guid.Empty && !hosts.Contains(id))
			{
				hosts.Add(id);
			}
		}

		return hosts;
	}

	private static string? FormatJumpHosts(IReadOnlyList<Guid> hosts) =>
		hosts.Count == 0 ? null : string.Join(',', hosts.Take(MaxJumpHosts).Select(id => id.ToString("D", CultureInfo.InvariantCulture)));

	private static int? ParseSeconds(string? value) =>
		int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) ? seconds : null;

	private static SshElevationMode ParseElevation(string? value) =>
		Enum.TryParse(value, ignoreCase: true, out SshElevationMode mode) && Enum.IsDefined(mode) ? mode : SshElevationMode.Auto;

	private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
