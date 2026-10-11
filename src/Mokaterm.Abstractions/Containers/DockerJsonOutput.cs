using System.Text.Json;

namespace Mokaterm.Abstractions.Containers;

/// <summary>
/// Reads what a docker command printed as JSON. <c>--format '{{json .}}'</c> prints one object per line, while
/// <c>--format json</c> on a compose command prints one array on some versions and one object per line on others, so
/// both shapes are read the same way.
/// </summary>
public static class DockerJsonOutput
{
	/// <summary>
	/// Every object in <paramref name="output"/>, whether it holds one array or one object per line. A line that is not
	/// JSON (a warning some versions print on stdout) is skipped rather than failing the whole listing.
	/// </summary>
	public static IReadOnlyList<JsonElement> ReadObjects(string? output)
	{
		List<JsonElement> objects = [];
		if (string.IsNullOrWhiteSpace(output))
		{
			return objects;
		}

		string trimmed = output.Trim();

		// One value over several lines: an inspect array, or an indented object such as compose config prints.
		// Several objects one per line do not parse as one value, and are read line by line below.
		if (trimmed.StartsWith('[') || trimmed.StartsWith('{'))
		{
			switch (TryParse(trimmed))
			{
				case { ValueKind: JsonValueKind.Array } array:
					objects.AddRange(array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object));
					return objects;
				case { ValueKind: JsonValueKind.Object } single:
					objects.Add(single);
					return objects;
			}
		}

		foreach (string line in trimmed.Split('\n'))
		{
			string text = line.Trim();
			if (text.Length == 0)
			{
				continue;
			}

			switch (TryParse(text))
			{
				case { ValueKind: JsonValueKind.Object } item:
					objects.Add(item);
					break;
				case { ValueKind: JsonValueKind.Array } items:
					objects.AddRange(items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object));
					break;
			}
		}

		return objects;
	}

	/// <summary>The one JSON value in <paramref name="text"/>, or null when it is not JSON.</summary>
	public static JsonElement? TryParse(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		try
		{
			using JsonDocument document = JsonDocument.Parse(text);
			return document.RootElement.Clone();
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>A property as text: strings as they are, numbers and booleans as written, anything else empty.</summary>
	public static string Text(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value)
			? value.ValueKind switch
			{
				JsonValueKind.String => value.GetString() ?? "",
				JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
				_ => "",
			}
			: "";
}
