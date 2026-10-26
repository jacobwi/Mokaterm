using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Mokaterm.Modules.Rdp.Protocol;

// sspi-rs looks a Kerberos server up by DNS from the login's domain, and a short NetBIOS domain is no DNS name: on a real
// domain those lookups took 44 s to fail before NTLM took over, and NTLM's next message stalled the same way. Windows'
// own client asks the domain locator, so a short domain is turned into the domain's DNS name before CredSSP starts.
internal static class RdpKerberosRealm
{
	private static readonly TimeSpan LookupBudget = TimeSpan.FromSeconds(3);

	// A miss is kept only for a while, because a VPN that comes up later makes the domain reachable.
	private static readonly TimeSpan MissKeptFor = TimeSpan.FromMinutes(5);

	private static readonly ConcurrentDictionary<string, (RdpRealm? Realm, DateTime At)> Known = new(StringComparer.OrdinalIgnoreCase);

	public static async Task<RdpRealm?> FindAsync(string domain, string host, CancellationToken cancellationToken)
	{
		if (!IsShortDomain(domain, host) || IsLoopback(host))
		{
			return null;
		}

		if (Known.TryGetValue(domain, out (RdpRealm? Realm, DateTime At) kept)
			&& (kept.Realm is not null || DateTime.UtcNow - kept.At < MissKeptFor))
		{
			return kept.Realm;
		}

		RdpRealm? realm = FromHostName(domain, host)
			?? await FromLocatorAsync(domain, cancellationToken)
			?? await FromHostLookupAsync(domain, host, cancellationToken);
		Known[domain] = (realm, DateTime.UtcNow);
		return realm;
	}

	// The PC's own name as the domain is a local account on that PC, which no domain controller knows.
	public static bool IsShortDomain(string domain, string host) =>
		domain.Length > 0
		&& !domain.Contains('.', StringComparison.Ordinal)
		&& !domain.Equals(RdpLogin.EntraIdDomain, StringComparison.OrdinalIgnoreCase)
		&& !domain.Equals(RdpLogin.MicrosoftAccountDomain, StringComparison.OrdinalIgnoreCase)
		&& !domain.Equals(FirstLabel(host), StringComparison.OrdinalIgnoreCase);

	public static RdpRealm? FromHostName(string domain, string host)
	{
		if (IPAddress.TryParse(host, out _))
		{
			return null;
		}

		string[] labels = host.TrimEnd('.').Split('.');
		for (int i = 1; i < labels.Length - 1; i++)
		{
			if (labels[i].Equals(domain, StringComparison.OrdinalIgnoreCase))
			{
				return new RdpRealm(string.Join('.', labels[i..]), "the PC's name");
			}
		}

		return null;
	}

	private static async Task<RdpRealm?> FromLocatorAsync(string domain, CancellationToken cancellationToken)
	{
		try
		{
			// DsGetDcName takes no cancellation and can wait on a NetBIOS broadcast, so it is left behind when it is slow.
			string? name = await Task.Run(() => OperatingSystem.IsWindows() ? RdpDomainLocator.DnsNameOf(domain) : null, CancellationToken.None)
				.WaitAsync(LookupBudget, cancellationToken);
			return name is not null && name.Contains('.', StringComparison.Ordinal) ? new RdpRealm(name, "Windows' domain locator") : null;
		}
		catch (TimeoutException)
		{
			return null;
		}
	}

	private static async Task<RdpRealm?> FromHostLookupAsync(string domain, string host, CancellationToken cancellationToken)
	{
		if (host.Contains('.', StringComparison.Ordinal) || IPAddress.TryParse(host, out _))
		{
			return null;
		}

		try
		{
			IPHostEntry entry = await Dns.GetHostEntryAsync(host, cancellationToken).WaitAsync(LookupBudget, cancellationToken);
			return FromHostName(domain, entry.HostName) is { } realm ? realm with { Source = "the PC's name in DNS" } : null;
		}
		catch (Exception ex) when (ex is SocketException or TimeoutException or ArgumentException)
		{
			return null;
		}
	}

	private static bool IsLoopback(string host) =>
		host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
		|| (IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address));

	private static string FirstLabel(string host)
	{
		int dot = host.IndexOf('.', StringComparison.Ordinal);
		return dot < 0 ? host : host[..dot];
	}
}
