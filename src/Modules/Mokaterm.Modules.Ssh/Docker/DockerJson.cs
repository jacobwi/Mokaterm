using System.Globalization;
using System.Text.Json;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Lenient reads of docker's JSON. A field a version does not print, or prints as null, reads as empty instead of
/// failing: what <c>inspect</c> holds has grown over the years, and the page shows whatever is there.
/// </summary>
internal static class DockerJson
{
	/// <summary>The object at <paramref name="path"/> under <paramref name="element"/>, or an undefined element.</summary>
	public static JsonElement Get(JsonElement element, params string[] path)
	{
		JsonElement current = element;
		foreach (string name in path)
		{
			if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out JsonElement next))
			{
				return default;
			}

			current = next;
		}

		return current;
	}

	public static string Text(JsonElement element, string name) => DockerJsonOutput.Text(element, name);

	/// <summary>The first object in a command's output, or an undefined element when it printed none.</summary>
	public static JsonElement First(string? output)
	{
		IReadOnlyList<JsonElement> objects = DockerJsonOutput.ReadObjects(output);
		return objects.Count > 0 ? objects[0] : default;
	}

	public static int? Int(JsonElement element, string name)
	{
		JsonElement value = Get(element, name);
		return value.ValueKind switch
		{
			JsonValueKind.Number when value.TryGetInt32(out int number) => number,
			JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) => parsed,
			_ => null,
		};
	}

	public static long? Long(JsonElement element, string name)
	{
		JsonElement value = Get(element, name);
		return value.ValueKind switch
		{
			JsonValueKind.Number when value.TryGetInt64(out long number) => number,
			JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) => parsed,
			_ => null,
		};
	}

	public static bool Bool(JsonElement element, string name)
	{
		JsonElement value = Get(element, name);
		return value.ValueKind == JsonValueKind.True
			|| (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool parsed) && parsed);
	}

	/// <summary>An array of strings, or one string as a list of one: old daemons printed <c>Cmd</c> either way.</summary>
	public static IReadOnlyList<string> Strings(JsonElement element, string name)
	{
		JsonElement value = Get(element, name);
		return value.ValueKind switch
		{
			JsonValueKind.Array => [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? "")],
			JsonValueKind.String when value.GetString() is { Length: > 0 } single => [single],
			_ => [],
		};
	}

	/// <summary>A string-to-string object such as <c>Labels</c>.</summary>
	public static IReadOnlyDictionary<string, string> Map(JsonElement element, string name)
	{
		Dictionary<string, string> map = new(StringComparer.Ordinal);
		JsonElement value = Get(element, name);
		if (value.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty property in value.EnumerateObject())
			{
				map[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : property.Value.GetRawText();
			}
		}

		return map;
	}

	/// <summary>An RFC 3339 time. Docker writes <c>0001-01-01T00:00:00Z</c> for "never", which reads as null.</summary>
	public static DateTimeOffset? Time(JsonElement element, string name) => ParseTime(Text(element, name));

	public static DateTimeOffset? ParseTime(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)
			|| !DateTimeOffset.TryParse(SevenDigitFraction(text.Trim()), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset time))
		{
			return null;
		}

		return time.Year <= 1 ? null : time;
	}

	/// <summary>Docker writes nanoseconds; .NET reads at most seven digits of a fraction, which is all a tick holds.</summary>
	private static string SevenDigitFraction(string text)
	{
		int dot = text.IndexOf('.', StringComparison.Ordinal);
		if (dot < 0)
		{
			return text;
		}

		int end = dot + 1;
		while (end < text.Length && char.IsAsciiDigit(text[end]))
		{
			end++;
		}

		return end - dot - 1 > 7 ? string.Concat(text.AsSpan(0, dot + 8), text.AsSpan(end)) : text;
	}

	/// <summary>
	/// The <c>CreatedAt</c> text <c>ps</c>, <c>images</c> and <c>network ls</c> print: <c>2024-01-02 03:04:05 +0000 UTC</c>.
	/// The trailing zone name is noise next to the offset.
	/// </summary>
	public static DateTimeOffset? ParseListingTime(string? text)
	{
		string[] parts = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 3)
		{
			return ParseTime(text);
		}

		string offset = parts[2].Length == 5 ? $"{parts[2][..3]}:{parts[2][3..]}" : parts[2];
		return DateTimeOffset.TryParse($"{parts[0]}T{parts[1]}{offset}", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset time)
			? time
			: null;
	}
}
