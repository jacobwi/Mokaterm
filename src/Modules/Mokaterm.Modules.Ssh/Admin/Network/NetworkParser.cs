using System.Globalization;
using System.Text.Json;

namespace Mokaterm.Modules.Ssh.Admin.Network;

/// <summary>Reads <c>ip -j</c> JSON, plain <c>ip</c> text, <c>ss</c> or <c>netstat</c>, and the resolver's settings.</summary>
internal static class NetworkParser
{
	private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = 16, AllowTrailingCommas = true };

	public static NetworkSnapshot Parse(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		IReadOnlyList<NetInterface>? interfaces = ParseAddressJson(sections.Get("addr-json"));
		bool fromText = interfaces is null;
		interfaces ??= ParseAddressText(sections.Get("addr-text"));

		List<NetRoute> routes = [];
		if (ParseRouteJson(sections.Get("route4-json"), ipv6: false) is { } ipv4)
		{
			routes.AddRange(ipv4);
			routes.AddRange(ParseRouteJson(sections.Get("route6-json"), ipv6: true) ?? []);
		}
		else
		{
			routes.AddRange(ParseRouteText(sections.Get("route-text")));
		}

		(List<(string Scope, IReadOnlyList<string> Servers)> dns, List<string> search) = ParseDns(sections);
		(IReadOnlyList<ListeningSocket> listening, string source) = ParseListening(sections);
		return new NetworkSnapshot
		{
			Interfaces = interfaces,
			Routes = routes,
			Dns = dns,
			SearchDomains = search,
			Listening = listening,
			ListeningSource = source,
			FromText = fromText,
		};
	}

	/// <summary>What <see cref="NetworkCommands.ListeningPorts"/> printed, and which tool printed it (empty for neither).</summary>
	public static (IReadOnlyList<ListeningSocket> Sockets, string Source) ParseListening(AdminSections sections)
	{
		ArgumentNullException.ThrowIfNull(sections);
		return sections.Has("ss") ? (ParseSs(sections.Get("ss")), "ss")
			: sections.Has("netstat") ? (ParseNetstat(sections.Get("netstat")), "netstat")
			: ([], "");
	}

	/// <summary><c>ip -j addr show</c>, or null when there is no JSON to read.</summary>
	public static IReadOnlyList<NetInterface>? ParseAddressJson(string? text)
	{
		if (!TryParseArray(text, out JsonDocument? document))
		{
			return null;
		}

		using (document)
		{
			List<NetInterface> interfaces = [];
			foreach (JsonElement item in document.RootElement.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.Object || String(item, "ifname") is not { Length: > 0 } name)
				{
					continue;
				}

				List<NetAddress> addresses = [];
				if (item.TryGetProperty("addr_info", out JsonElement info) && info.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement address in info.EnumerateArray())
					{
						if (address.ValueKind == JsonValueKind.Object && String(address, "local") is { Length: > 0 } local)
						{
							addresses.Add(new NetAddress(String(address, "family") ?? "", local, Int(address, "prefixlen") ?? 0, String(address, "scope") ?? ""));
						}
					}
				}

				interfaces.Add(new NetInterface
				{
					Name = name,
					State = String(item, "operstate") ?? "",
					Flags = Strings(item, "flags"),
					Mtu = Int(item, "mtu"),
					Mac = String(item, "address") ?? "",
					LinkType = String(item, "link_type") ?? "",
					Addresses = addresses,
				});
			}

			return interfaces;
		}
	}

	/// <summary>
	/// Plain <c>ip addr show</c>: a numbered header line per interface (<c>2: eth0@if5: &lt;FLAGS&gt; mtu 1500 ... state UP</c>)
	/// followed by indented <c>link/</c>, <c>inet</c> and <c>inet6</c> lines.
	/// </summary>
	public static IReadOnlyList<NetInterface> ParseAddressText(string? text)
	{
		List<NetInterface> interfaces = [];
		NetInterface? current = null;
		List<NetAddress> addresses = [];
		foreach (string line in AdminSections.Lines(text))
		{
			if (line.Length > 0 && char.IsAsciiDigit(line[0]))
			{
				Flush();
				string[] parts = line.Split(':', 3);
				if (parts.Length < 3)
				{
					continue;
				}

				string name = parts[1].Trim();
				int at = name.IndexOf('@', StringComparison.Ordinal);
				string rest = parts[2];
				int open = rest.IndexOf('<', StringComparison.Ordinal);
				int close = rest.IndexOf('>', StringComparison.Ordinal);
				string[] words = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				current = new NetInterface
				{
					Name = at > 0 ? name[..at] : name,
					Flags = open >= 0 && close > open ? rest[(open + 1)..close].Split(',', StringSplitOptions.RemoveEmptyEntries) : [],
					Mtu = After(words, "mtu") is { } mtu && int.TryParse(mtu, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null,
					State = After(words, "state") ?? "",
				};
				continue;
			}

			if (current is null)
			{
				continue;
			}

			string[] fields = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (fields.Length >= 2 && fields[0].StartsWith("link/", StringComparison.Ordinal))
			{
				current = current with { LinkType = fields[0][5..], Mac = fields[1] };
			}
			else if (fields.Length >= 2 && fields[0] is "inet" or "inet6")
			{
				string[] cidr = fields[1].Split('/');
				int prefix = cidr.Length > 1 && int.TryParse(cidr[1], NumberStyles.None, CultureInfo.InvariantCulture, out int length) ? length : 0;
				addresses.Add(new NetAddress(fields[0], cidr[0], prefix, After(fields, "scope") ?? ""));
			}
		}

		Flush();
		return interfaces;

		void Flush()
		{
			if (current is not null)
			{
				interfaces.Add(current with { Addresses = [.. addresses] });
			}

			current = null;
			addresses.Clear();
		}
	}

	/// <summary><c>ip -j route show</c>, or null when there is no JSON to read.</summary>
	public static IReadOnlyList<NetRoute>? ParseRouteJson(string? text, bool ipv6)
	{
		if (!TryParseArray(text, out JsonDocument? document))
		{
			return null;
		}

		using (document)
		{
			List<NetRoute> routes = [];
			foreach (JsonElement item in document.RootElement.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.Object && String(item, "dst") is { Length: > 0 } destination)
				{
					routes.Add(new NetRoute
					{
						Destination = destination,
						Gateway = String(item, "gateway") ?? "",
						Device = String(item, "dev") ?? "",
						Protocol = String(item, "protocol") ?? "",
						Source = String(item, "prefsrc") ?? "",
						Metric = Int(item, "metric"),
						IsIpv6 = ipv6,
					});
				}
			}

			return routes;
		}
	}

	/// <summary>Plain <c>ip route</c> lines: <c>default via 10.0.0.1 dev eth0 proto dhcp src 10.0.0.5 metric 100</c>.</summary>
	public static IReadOnlyList<NetRoute> ParseRouteText(string? text)
	{
		List<NetRoute> routes = [];
		foreach (string line in AdminSections.Lines(text))
		{
			string[] words = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (words.Length == 0)
			{
				continue;
			}

			routes.Add(new NetRoute
			{
				Destination = words[0],
				Gateway = After(words, "via") ?? "",
				Device = After(words, "dev") ?? "",
				Protocol = After(words, "proto") ?? "",
				Source = After(words, "src") ?? "",
				Metric = After(words, "metric") is { } metric && int.TryParse(metric, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null,
				IsIpv6 = words[0].Contains(':', StringComparison.Ordinal) || (After(words, "via") ?? "").Contains(':', StringComparison.Ordinal),
			});
		}

		return routes;
	}

	/// <summary>
	/// <c>ss -tulpn</c>: <c>Netid State Recv-Q Send-Q Local:Port Peer:Port [Process]</c>, with the process as
	/// <c>users:(("sshd",pid=800,fd=3))</c>.
	/// </summary>
	public static IReadOnlyList<ListeningSocket> ParseSs(string? text)
	{
		List<ListeningSocket> sockets = [];
		foreach (string line in AdminSections.Lines(text))
		{
			string[] words = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if (words.Length < 5 || !(words[0].StartsWith("tcp", StringComparison.Ordinal) || words[0].StartsWith("udp", StringComparison.Ordinal)))
			{
				continue;
			}

			if (SplitEndpoint(words[4]) is not ({ } address, { } port))
			{
				continue;
			}

			(string process, int? pid) = words.Length > 6 ? SsProcess(string.Join(' ', words[6..])) : ("", null);
			sockets.Add(new ListeningSocket { Protocol = words[0], Address = address, Port = port, Process = process, Pid = pid });
		}

		return Ordered(sockets);
	}

	/// <summary><c>netstat -tulpn</c>: <c>Proto Recv-Q Send-Q Local Foreign [State] PID/Program</c>; UDP lines have no state.</summary>
	public static IReadOnlyList<ListeningSocket> ParseNetstat(string? text)
	{
		List<ListeningSocket> sockets = [];
		foreach (string line in AdminSections.Lines(text))
		{
			string[] words = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if (words.Length < 5 || !(words[0].StartsWith("tcp", StringComparison.Ordinal) || words[0].StartsWith("udp", StringComparison.Ordinal)))
			{
				continue;
			}

			if (SplitEndpoint(words[3]) is not ({ } address, { } port))
			{
				continue;
			}

			string program = words[^1].Contains('/', StringComparison.Ordinal) ? words[^1] : "";
			int slash = program.IndexOf('/', StringComparison.Ordinal);
			int? pid = slash > 0 && int.TryParse(program[..slash], NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null;
			sockets.Add(new ListeningSocket
			{
				Protocol = words[0],
				Address = address,
				Port = port,
				Process = slash > 0 ? program[(slash + 1)..] : "",
				Pid = pid,
			});
		}

		return Ordered(sockets);
	}

	/// <summary><c>host:port</c> with IPv6 in brackets and an optional <c>%interface</c>; the port is after the last colon.</summary>
	internal static (string? Address, int? Port) SplitEndpoint(string endpoint)
	{
		int colon = endpoint.LastIndexOf(':');
		if (colon <= 0 || !int.TryParse(endpoint[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int port))
		{
			return (null, null);
		}

		string address = endpoint[..colon];
		if (address.StartsWith('[') && address.EndsWith(']'))
		{
			address = address[1..^1];
		}

		return (address, port);
	}

	/// <summary>True when <paramref name="address"/> is one of the interface's addresses, an IPv4 mapped into IPv6 included.</summary>
	public static bool Holds(NetInterface networkInterface, string? address)
	{
		ArgumentNullException.ThrowIfNull(networkInterface);
		if (string.IsNullOrEmpty(address))
		{
			return false;
		}

		string wanted = Normalize(address);
		return networkInterface.Addresses.Any(candidate => Normalize(candidate.Address) == wanted);
	}

	private static string Normalize(string address)
	{
		string trimmed = address.Trim().Trim('[', ']');
		int zone = trimmed.IndexOf('%', StringComparison.Ordinal);
		if (zone >= 0)
		{
			trimmed = trimmed[..zone];
		}

		if (System.Net.IPAddress.TryParse(trimmed, out System.Net.IPAddress? parsed))
		{
			return (parsed.IsIPv4MappedToIPv6 ? parsed.MapToIPv4() : parsed).ToString();
		}

		return trimmed.ToLowerInvariant();
	}

	private static (List<(string Scope, IReadOnlyList<string> Servers)> Dns, List<string> Search) ParseDns(AdminSections sections)
	{
		List<(string Scope, IReadOnlyList<string> Servers)> dns = [];
		List<string> search = [];
		foreach (string line in AdminSections.Lines(sections.Get("resolvectl")))
		{
			int colon = line.IndexOf("):", StringComparison.Ordinal) is var link and > 0 ? link + 1 : line.IndexOf(':', StringComparison.Ordinal);
			if (colon <= 0)
			{
				continue;
			}

			string[] servers = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (servers.Length > 0)
			{
				dns.Add((LinkScope(line[..colon]), servers));
			}
		}

		foreach (string line in AdminSections.Lines(sections.Get("resolvectl-domain")))
		{
			int colon = line.LastIndexOf(':');
			if (colon > 0)
			{
				search.AddRange(line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries));
			}
		}

		List<string> fileServers = [];
		foreach (string line in AdminSections.Lines(sections.Get("resolv")))
		{
			string[] words = line.Trim().Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if (words.Length >= 2 && words[0] == "nameserver")
			{
				fileServers.Add(words[1]);
			}
			else if (words.Length >= 2 && words[0] is "search" or "domain")
			{
				search.AddRange(words[1..]);
			}
		}

		// With systemd-resolved the file names only its stub (127.0.0.53), which says nothing a reader can use.
		if (fileServers.Count > 0 && (dns.Count == 0 || fileServers.Any(server => server != "127.0.0.53" && server != "127.0.0.54")))
		{
			dns.Add(("resolv.conf", fileServers));
		}

		return (dns, [.. search.Distinct(StringComparer.OrdinalIgnoreCase)]);
	}

	/// <summary>"Link 2 (eth0)" becomes "eth0"; "Global" stays.</summary>
	private static string LinkScope(string scope)
	{
		int open = scope.IndexOf('(', StringComparison.Ordinal);
		int close = scope.IndexOf(')', StringComparison.Ordinal);
		return open >= 0 && close > open ? scope[(open + 1)..close] : scope.Trim();
	}

	private static (string Process, int? Pid) SsProcess(string text)
	{
		int quote = text.IndexOf('"', StringComparison.Ordinal);
		int end = quote < 0 ? -1 : text.IndexOf('"', quote + 1);
		string name = quote >= 0 && end > quote ? text[(quote + 1)..end] : "";
		int pidAt = text.IndexOf("pid=", StringComparison.Ordinal);
		int? pid = null;
		if (pidAt >= 0)
		{
			int stop = pidAt + 4;
			while (stop < text.Length && char.IsAsciiDigit(text[stop]))
			{
				stop++;
			}

			pid = int.TryParse(text[(pidAt + 4)..stop], NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null;
		}

		return (name, pid);
	}

	private static List<ListeningSocket> Ordered(List<ListeningSocket> sockets)
	{
		sockets.Sort((left, right) => left.Port != right.Port
			? left.Port.CompareTo(right.Port)
			: string.CompareOrdinal(left.Protocol, right.Protocol));
		return sockets;
	}

	private static string? After(string[] words, string key)
	{
		int index = Array.IndexOf(words, key);
		return index >= 0 && index + 1 < words.Length ? words[index + 1] : null;
	}

	private static bool TryParseArray(string? text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out JsonDocument? document)
	{
		document = null;
		string trimmed = text?.Trim() ?? "";
		if (!trimmed.StartsWith('['))
		{
			return false;
		}

		try
		{
			document = JsonDocument.Parse(trimmed, JsonOptions);
		}
		catch (JsonException)
		{
			return false;
		}

		if (document.RootElement.ValueKind != JsonValueKind.Array)
		{
			document.Dispose();
			document = null;
			return false;
		}

		return true;
	}

	private static string? String(JsonElement element, string name) =>
		element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	private static int? Int(JsonElement element, string name) =>
		element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;

	private static IReadOnlyList<string> Strings(JsonElement element, string name) =>
		element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
			? [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? "")]
			: [];
}
