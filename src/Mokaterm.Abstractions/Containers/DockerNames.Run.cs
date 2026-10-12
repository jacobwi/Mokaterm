using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Mokaterm.Abstractions.Containers;

/// <summary>
/// The rest of what <c>docker run</c> and <c>docker exec</c> are given: published ports, mounts, environment names and
/// restart policies. Each method returns why the value cannot be used, or null when it can, like the name rules.
/// </summary>
public static partial class DockerNames
{
	/// <summary>The most retries <c>on-failure</c> is given here: past this it is a restart loop, not a policy.</summary>
	public const int MaxRestartRetries = 1000;

	private static readonly HashSet<string> MountOptions = new(StringComparer.Ordinal)
	{
		"ro", "rw", "z", "Z", "nocopy", "shared", "slave", "private", "rshared", "rslave", "rprivate",
	};

	/// <summary>
	/// A published port, <c>[ip:]host:container[/proto]</c>: <c>8080:80</c>, <c>127.0.0.1:8443:443/tcp</c>,
	/// <c>[::1]:5353:53/udp</c>. Either side may be a range; a host range may go with one container port (docker picks a
	/// free one in it), and two ranges must be the same length.
	/// </summary>
	public static string? PortMappingProblem(string? mapping)
	{
		string text = mapping?.Trim() ?? "";
		if (text.Length == 0)
		{
			return "Name a port, such as 8080:80.";
		}

		Match match = text.Length <= 128 ? PortMapping().Match(text) : Match.Empty;
		if (!match.Success)
		{
			return "A port is [ip:]host:container[/tcp|udp|sctp], such as 8080:80 or 127.0.0.1:8443:443/tcp.";
		}

		if (match.Groups["ip"].Success && !IsAddress(match.Groups["ip"].Value))
		{
			return $"{match.Groups["ip"].Value} is not an IP address.";
		}

		if (PortRange(match.Groups["host"].Value) is not { } host)
		{
			return $"The host port {match.Groups["host"].Value} is not a port from 1 to 65535, or a range of them.";
		}

		if (PortRange(match.Groups["container"].Value) is not { } container)
		{
			return $"The container port {match.Groups["container"].Value} is not a port from 1 to 65535, or a range of them.";
		}

		int hostCount = host.Last - host.First;
		int containerCount = container.Last - container.First;
		return containerCount == 0 || hostCount == containerCount
			? null
			: "A range of container ports needs a host range of the same length.";
	}

	/// <summary>
	/// A mount: an absolute path in the container alone (an anonymous volume), or <c>source:target[:options]</c> where
	/// the source is an absolute path on the docker host (a bind) or a volume name. Options are docker's own, comma
	/// separated: <c>ro</c>, <c>rw</c>, <c>z</c>, <c>Z</c>, <c>nocopy</c> and the propagation modes.
	/// </summary>
	public static string? MountProblem(string? mount)
	{
		string text = mount?.Trim() ?? "";
		if (text.Length == 0)
		{
			return "Name a volume or a folder, such as data:/var/lib/data.";
		}

		string[] parts = text.Split(':');
		if (parts.Length > 3)
		{
			return "A mount is source:target[:options]; a path holding ':' cannot be mounted this way.";
		}

		if (parts.Length == 1)
		{
			return PathProblem(parts[0], "path in the container");
		}

		string source = parts[0];
		string? sourceProblem = source.StartsWith('/')
			? PathProblem(source, "folder on the docker host")
			: NewNameProblem(source, "volume");
		if (sourceProblem is not null)
		{
			return sourceProblem;
		}

		if (PathProblem(parts[1], "path in the container") is { } targetProblem)
		{
			return targetProblem;
		}

		if (parts.Length == 3)
		{
			string[] options = parts[2].Split(',');
			if (options.FirstOrDefault(option => !MountOptions.Contains(option)) is { } unknown)
			{
				return $"{(unknown.Length == 0 ? "An empty option" : unknown)} is not a mount option: use ro, rw, z, Z, nocopy or a propagation mode.";
			}

			if (options.Contains("ro") && options.Contains("rw"))
			{
				return "A mount is either ro or rw, not both.";
			}
		}

		return null;
	}

	/// <summary>An environment variable's name: letters, digits and '_', not starting with a digit.</summary>
	public static string? VariableProblem(string? name)
	{
		string text = name ?? "";
		if (text.Length == 0)
		{
			return "Name the variable.";
		}

		return VariableName().IsMatch(text)
			? null
			: "A variable is named with letters, digits and '_', and does not start with a digit.";
	}

	/// <summary>An <c>on-failure</c> retry count: 0 means no limit.</summary>
	public static string? RestartRetriesProblem(int retries) =>
		retries is >= 0 and <= MaxRestartRetries
			? null
			: string.Create(CultureInfo.CurrentCulture, $"Retries run from 0 (no limit) to {MaxRestartRetries}.");

	private static bool IsAddress(string text)
	{
		string bare = text.StartsWith('[') && text.EndsWith(']') ? text[1..^1] : text;
		if (!IPAddress.TryParse(bare, out IPAddress? address))
		{
			return false;
		}

		// IPAddress reads "1" as 0.0.0.1, so the written form has to be the address it claims to be.
		return address.AddressFamily == AddressFamily.InterNetworkV6 ? text.StartsWith('[') : !text.StartsWith('[') && text.Count(character => character == '.') == 3;
	}

	private static (int First, int Last)? PortRange(string text)
	{
		int dash = text.IndexOf('-', StringComparison.Ordinal);
		string first = dash < 0 ? text : text[..dash];
		string last = dash < 0 ? text : text[(dash + 1)..];
		return Port(first) is { } low && Port(last) is { } high && low <= high ? (low, high) : null;
	}

	private static int? Port(string text) =>
		int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is >= 1 and <= 65535 ? port : null;

	[GeneratedRegex(@"^(?:(?<ip>\[[0-9A-Fa-f:.]+\]|[0-9.]+):)?(?<host>[0-9]{1,5}(?:-[0-9]{1,5})?):(?<container>[0-9]{1,5}(?:-[0-9]{1,5})?)(?:/(?:tcp|udp|sctp))?\z", RegexOptions.CultureInvariant)]
	private static partial Regex PortMapping();

	[GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,127}\z", RegexOptions.CultureInvariant)]
	private static partial Regex VariableName();
}
