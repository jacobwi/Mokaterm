using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mokaterm.Modules.Ssh.Docker;

// What one read of config --hash found: each service's hash as the files read now, the services whose hash rests on
// a variable this shell does not set, or why nothing could be told at all.
internal sealed partial record ComposeHashes
{
	public IReadOnlyDictionary<string, string> Services { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

	public IReadOnlyDictionary<string, string> Uncertain { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

	public string? Problem { get; init; }

	public required DateTimeOffset ReadAt { get; init; }

	// The compose that read them, since a switch between the plugin and the standalone program reads them anew.
	public required string Tool { get; init; }

	public static ComposeHashes Failed(string problem, DateTimeOffset readAt, string tool) => new() { Problem = problem, ReadAt = readAt, Tool = tool };

	// "web 3f2a..." lines, which 2.x and 1.x print alike; anything else on stdout is left out.
	public static IReadOnlyDictionary<string, string> ParseLines(string? output)
	{
		Dictionary<string, string> hashes = new(StringComparer.Ordinal);
		foreach (string line in (output ?? "").Split('\n'))
		{
			Match match = HashLine().Match(line.TrimEnd('\r'));
			if (match.Success)
			{
				hashes[match.Groups["service"].Value] = match.Groups["hash"].Value;
			}
		}

		return hashes;
	}

	// The services of config --no-interpolate --format json that name one of the variables, as $NAME or ${NAME...}.
	// A $$NAME (a literal dollar) counts too: that only ever leaves a service unknown, never wrongly changed.
	public static IReadOnlyDictionary<string, string> ServicesUsing(string? json, IReadOnlyList<string> variables)
	{
		ArgumentNullException.ThrowIfNull(variables);
		Dictionary<string, string> found = new(StringComparer.Ordinal);
		JsonElement services = DockerJson.Get(DockerJson.First(json), "services");
		if (services.ValueKind != JsonValueKind.Object || variables.Count == 0)
		{
			return found;
		}

		foreach (JsonProperty service in services.EnumerateObject())
		{
			string text = service.Value.GetRawText();
			string[] used = [.. variables.Where(variable => Names(text, variable))];
			if (used.Length > 0)
			{
				found[service.Name] = Uses(service.Name, used);
			}
		}

		return found;
	}

	public static bool HasServices(string? json) =>
		DockerJson.Get(DockerJson.First(json), "services").ValueKind == JsonValueKind.Object;

	// What an unknown service says when its hash rests on variables this shell does not set.
	public static string Uses(string service, IReadOnlyList<string> variables)
	{
		ArgumentNullException.ThrowIfNull(variables);
		string names = string.Join(", ", variables);
		return variables.Count == 1
			? $"{service} uses {names}, which is not set where the manager runs compose, so its hash may differ from the one up worked out."
			: $"{service} uses {names}, which are not set where the manager runs compose, so its hash may differ from the one up worked out.";
	}

	private static bool Names(string text, string variable)
	{
		int at = 0;
		while ((at = text.IndexOf(variable, at, StringComparison.Ordinal)) >= 0)
		{
			int end = at + variable.Length;
			bool bare = at >= 1 && text[at - 1] == '$';
			bool braced = at >= 2 && text[at - 1] == '{' && text[at - 2] == '$';
			bool whole = end >= text.Length || !(char.IsAsciiLetterOrDigit(text[end]) || text[end] == '_');
			if ((bare || braced) && whole)
			{
				return true;
			}

			at = end;
		}

		return false;
	}

	[GeneratedRegex(@"^(?<service>[a-zA-Z0-9][a-zA-Z0-9._-]{0,254}) (?<hash>[0-9a-f]{64})\z", RegexOptions.CultureInvariant)]
	private static partial Regex HashLine();
}
