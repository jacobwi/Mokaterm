using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Mokaterm.Core.Terminal;

/// <summary>JSON handling shared by the Windows Terminal and VS Code codecs.</summary>
internal static class TerminalThemeJson
{
	// Both formats live in files people edit by hand, where comments and trailing commas are normal.
	private static readonly JsonDocumentOptions ReadOptions = new()
	{
		CommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		MaxDepth = 64,
	};

	public static bool TryParse(string text, [NotNullWhen(true)] out JsonDocument? document, [NotNullWhen(false)] out string? error)
	{
		try
		{
			document = JsonDocument.Parse(text, ReadOptions);
			error = null;
			return true;
		}
		catch (JsonException)
		{
			document = null;
			error = "This is not valid JSON.";
			return false;
		}
	}

	/// <summary>The property named <paramref name="name"/>, ignoring case, or null.</summary>
	public static JsonElement? Property(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		foreach (JsonProperty property in element.EnumerateObject())
		{
			if (property.NameEquals(name) || string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				return property.Value;
			}
		}

		return null;
	}

	/// <summary>Every string property of <paramref name="element"/>, keyed without regard to case.</summary>
	public static Dictionary<string, string> StringProperties(JsonElement element)
	{
		Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
		if (element.ValueKind != JsonValueKind.Object)
		{
			return values;
		}

		foreach (JsonProperty property in element.EnumerateObject())
		{
			if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } value)
			{
				values[property.Name] = value;
			}
		}

		return values;
	}

	public static string Write(Action<Utf8JsonWriter> body)
	{
		ArrayBufferWriter<byte> buffer = new();
		using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
		{
			body(writer);
		}

		// The writer indents with the platform newline; exported schemes keep line feeds so the files travel.
		return Encoding.UTF8.GetString(buffer.WrittenSpan).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
	}
}
