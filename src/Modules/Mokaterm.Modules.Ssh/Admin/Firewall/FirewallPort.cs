using System.Globalization;

namespace Mokaterm.Modules.Ssh.Admin.Firewall;

/// <summary>A port and a protocol, <c>tcp</c> or <c>udp</c>, or both when <see cref="Protocol"/> is empty.</summary>
internal sealed record FirewallPort(int Port, string Protocol)
{
	/// <summary>As ufw and firewalld write it: <c>8080/tcp</c>, or <c>8080</c> for both.</summary>
	public override string ToString() => Protocol.Length == 0
		? Port.ToString(CultureInfo.InvariantCulture)
		: string.Create(CultureInfo.InvariantCulture, $"{Port}/{Protocol}");

	/// <summary>A port from 1 to 65535, optionally followed by <c>/tcp</c> or <c>/udp</c>.</summary>
	public static FirewallPort? TryParse(string? text, out string? problem)
	{
		problem = null;
		string trimmed = (text ?? "").Trim().ToLowerInvariant();
		string[] parts = trimmed.Split('/');
		if (parts.Length > 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
		{
			problem = "Type a port from 1 to 65535, such as 8080 or 8080/tcp.";
			return null;
		}

		string protocol = parts.Length == 2 ? parts[1] : "";
		if (protocol is not ("" or "tcp" or "udp"))
		{
			problem = "The protocol is tcp or udp.";
			return null;
		}

		return new FirewallPort(port, protocol);
	}

	/// <summary>Whether a rule for this port touches <paramref name="port"/> over TCP, the way SSH runs.</summary>
	public bool CoversTcp(int port) => Port == port && Protocol is "" or "tcp";
}
