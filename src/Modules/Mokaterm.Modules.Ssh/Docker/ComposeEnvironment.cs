using System.Text.Json;
using System.Text.RegularExpressions;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// What feeds a project's variables: the env files its services name (<c>env_file:</c>), read from
/// <c>config --format json</c> where compose prints it and from the YAML otherwise, and the variables compose warns
/// nothing sets.
/// </summary>
internal static partial class ComposeEnvironment
{
	/// <summary>
	/// Every env file the services of <c>config --format json</c> name, absolute (compose resolves them), each once and
	/// in the order met. <c>env_file</c> is a string, a list of strings, or (since 2.24) a list of
	/// <c>{"path": ..., "required": ...}</c>.
	/// </summary>
	public static IReadOnlyList<string> EnvFilesFromConfigJson(string? json)
	{
		List<string> files = [];
		JsonElement root = DockerJson.First(json);
		JsonElement services = DockerJson.Get(root, "services");
		if (services.ValueKind != JsonValueKind.Object)
		{
			return files;
		}

		foreach (JsonProperty service in services.EnumerateObject())
		{
			JsonElement envFile = DockerJson.Get(service.Value, "env_file");
			IEnumerable<JsonElement> entries = envFile.ValueKind switch
			{
				JsonValueKind.Array => envFile.EnumerateArray(),
				JsonValueKind.String or JsonValueKind.Object => [envFile],
				_ => [],
			};

			foreach (JsonElement entry in entries)
			{
				string path = entry.ValueKind == JsonValueKind.String ? entry.GetString() ?? "" : DockerJson.Text(entry, "path");
				if (path.Length > 0 && !files.Contains(path, StringComparer.Ordinal))
				{
					files.Add(path);
				}
			}
		}

		return files;
	}

	/// <summary>
	/// The <c>env_file:</c> entries of a compose file's YAML, read conservatively for a compose that prints no JSON:
	/// the inline forms (<c>env_file: a.env</c>, <c>env_file: [a.env, b.env]</c>) and the block list under the key, with
	/// <c>- path: x</c> items as well as plain ones. Relative paths are resolved against the file's own folder, which is
	/// what compose resolves them against. Anything else is left alone rather than guessed at.
	/// </summary>
	public static IReadOnlyList<string> EnvFilesFromYaml(string? yaml, string composeFile)
	{
		ArgumentNullException.ThrowIfNull(composeFile);
		string folder = DockerHostPaths.DirectoryOf(composeFile);
		List<string> files = [];
		string[] lines = (yaml ?? "").ReplaceLineEndings("\n").Split('\n');
		for (int index = 0; index < lines.Length; index++)
		{
			Match key = EnvFileKey().Match(lines[index]);
			if (!key.Success)
			{
				continue;
			}

			int indent = key.Groups["indent"].Length;
			string inline = StripComment(key.Groups["rest"].Value).Trim();
			if (inline.StartsWith('[') && inline.EndsWith(']'))
			{
				foreach (string item in inline[1..^1].Split(','))
				{
					Add(files, folder, item);
				}

				continue;
			}

			if (inline.Length > 0)
			{
				Add(files, folder, inline);
				continue;
			}

			// The block list: lines indented past the key, "- x" or "- path: x" items, until the indent drops back.
			for (index++; index < lines.Length; index++)
			{
				string line = lines[index];
				if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
				{
					continue;
				}

				int lineIndent = line.Length - line.TrimStart().Length;
				if (lineIndent <= indent)
				{
					index--;
					break;
				}

				string item = StripComment(line.Trim());
				if (item.StartsWith("- ", StringComparison.Ordinal))
				{
					string value = item[2..].Trim();
					Add(files, folder, value.StartsWith("path:", StringComparison.Ordinal) ? value[5..] : value);
				}
				else if (item.StartsWith("path:", StringComparison.Ordinal))
				{
					Add(files, folder, item[5..]);
				}
			}
		}

		return files;
	}

	/// <summary>
	/// The variables compose warned are not set, from what <c>config</c> printed on stderr: 2.x writes
	/// <c>The "NAME" variable is not set. Defaulting to a blank string.</c>, 1.x the same without the quotes. With no
	/// terminal (which is how the manager runs it) 2.x logs in logfmt, where the quotes come escaped: <c>The \"NAME\"</c>.
	/// </summary>
	public static IReadOnlyList<string> UnsetVariables(string? warnings) =>
		[.. UnsetWarning().Matches(warnings ?? "").Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal)];

	private static void Add(List<string> files, string folder, string raw)
	{
		string value = raw.Trim().Trim('"', '\'').Trim();
		if (value.Length == 0 || value.Any(char.IsControl) || value.Contains("${", StringComparison.Ordinal))
		{
			return;
		}

		if (DockerHostPaths.Resolve(folder, value) is { } path && DockerNames.PathProblem(path, "env file") is null && !files.Contains(path, StringComparer.Ordinal))
		{
			files.Add(path);
		}
	}

	/// <summary>A YAML comment starts at a <c>#</c> after a blank; one inside quotes is left alone by not being handled at all.</summary>
	private static string StripComment(string text)
	{
		for (int index = 0; index < text.Length; index++)
		{
			if (text[index] == '#' && (index == 0 || char.IsWhiteSpace(text[index - 1])))
			{
				return text[..index];
			}
		}

		return text;
	}

	[GeneratedRegex(@"^(?<indent>[ ]*)env_file:(?<rest>.*)$", RegexOptions.CultureInvariant)]
	private static partial Regex EnvFileKey();

	[GeneratedRegex("""The \\?"?(?<name>[A-Za-z_][A-Za-z0-9_]*)\\?"? variable is not set""", RegexOptions.CultureInvariant)]
	private static partial Regex UnsetWarning();
}
