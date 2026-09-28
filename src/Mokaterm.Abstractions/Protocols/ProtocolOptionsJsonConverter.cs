using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mokaterm.Abstractions.Protocols;

/// <summary>Stores <see cref="ProtocolOptions"/> as a flat JSON object of strings.</summary>
public sealed class ProtocolOptionsJsonConverter : JsonConverter<ProtocolOptions>
{
	public override ProtocolOptions Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return ProtocolOptions.Empty;
		}

		if (reader.TokenType != JsonTokenType.StartObject)
		{
			throw new JsonException("Protocol options must be a JSON object.");
		}

		List<KeyValuePair<string, string>> values = [];
		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.EndObject)
			{
				return ProtocolOptions.From(values);
			}

			string key = reader.GetString() ?? throw new JsonException("Protocol option keys cannot be null.");
			reader.Read();
			string? value = reader.TokenType switch
			{
				JsonTokenType.String => reader.GetString(),
				JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False => ReadRaw(ref reader),
				JsonTokenType.Null => null,
				_ => throw new JsonException($"Protocol option '{key}' must be a string."),
			};

			if (!string.IsNullOrEmpty(value))
			{
				values.Add(new KeyValuePair<string, string>(key, value));
			}
		}

		throw new JsonException("Unexpected end of protocol options.");
	}

	public override void Write(Utf8JsonWriter writer, ProtocolOptions value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		foreach (KeyValuePair<string, string> pair in value)
		{
			writer.WriteString(pair.Key, pair.Value);
		}

		writer.WriteEndObject();
	}

	private static string ReadRaw(ref Utf8JsonReader reader)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		return document.RootElement.GetRawText();
	}
}
