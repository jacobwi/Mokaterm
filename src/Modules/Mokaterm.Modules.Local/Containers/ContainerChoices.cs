using System.Globalization;
using System.Text.Json;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Local.Containers;

/// <summary>Reads what <c>docker ps</c> and <c>kubectl get pods</c> print into the choices the options editor offers.</summary>
internal static class ContainerChoices
{
	private const string RunningPhase = "Running";

	/// <summary>
	/// <c>docker ps --format "{{json .}}"</c>: one object per line. A line that is not JSON (a warning about a context,
	/// a deprecation notice) is skipped rather than failing the list.
	/// </summary>
	public static IReadOnlyList<ContainerChoice> FromDockerPs(string? output) =>
		[.. DockerContainerRow.Parse(output)
			.Where(row => row.Name.Length > 0)
			.Select(row => new ContainerChoice(
				row.Name,
				string.Join(" · ", new[] { row.Image, row.Status }.Where(part => part.Length > 0)),
				string.Equals(row.State, "running", StringComparison.Ordinal),
				null,
				[]))
			.OrderBy(choice => choice.Name, StringComparer.Ordinal)];

	/// <summary>
	/// <c>kubectl get pods -o json</c>: a List whose items carry <c>metadata.name</c>, <c>metadata.namespace</c>,
	/// <c>status.phase</c> and <c>spec.containers[].name</c>. Anything that does not parse gives an empty list.
	/// </summary>
	public static IReadOnlyList<ContainerChoice> FromKubectlPods(string? json)
	{
		List<ContainerChoice> choices = [];
		if (string.IsNullOrWhiteSpace(json) || DockerJsonOutput.TryParse(json.Trim()) is not { } root
			|| root.ValueKind != JsonValueKind.Object
			|| !root.TryGetProperty("items", out JsonElement items)
			|| items.ValueKind != JsonValueKind.Array)
		{
			return choices;
		}

		foreach (JsonElement item in items.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			JsonElement metadata = Child(item, "metadata");
			string name = DockerJsonOutput.Text(metadata, "name");
			if (name.Length == 0)
			{
				continue;
			}

			string phase = DockerJsonOutput.Text(Child(item, "status"), "phase");
			JsonElement containers = Child(Child(item, "spec"), "containers");
			List<string> names = containers.ValueKind == JsonValueKind.Array
				? [.. containers.EnumerateArray().Select(container => DockerJsonOutput.Text(container, "name")).Where(container => container.Length > 0)]
				: [];
			string detail = names.Count > 1
				? string.Create(CultureInfo.InvariantCulture, $"{phase} · {names.Count} containers")
				: phase;
			string space = DockerJsonOutput.Text(metadata, "namespace");
			choices.Add(new ContainerChoice(name, detail, phase == RunningPhase, space.Length == 0 ? null : space, names));
		}

		return [.. choices.OrderBy(choice => choice.Namespace, StringComparer.Ordinal).ThenBy(choice => choice.Name, StringComparer.Ordinal)];
	}

	private static JsonElement Child(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement child) ? child : default;
}
