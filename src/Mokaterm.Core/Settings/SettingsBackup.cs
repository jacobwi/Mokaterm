using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Core.Serialization;

namespace Mokaterm.Core.Settings;

/// <summary>
/// The settings document in a file of its own and back. The file names its format, the way the connections export
/// does, so a picked JSON file that is something else is refused by name. A section whose type is known (see
/// <see cref="SettingsSectionType"/>) is read through that type and the sanitizer before anything is replaced; one
/// from a module this host does not load travels as it is, the way settings.json keeps it.
/// </summary>
internal sealed class SettingsBackup : ISettingsBackup
{
	/// <summary>Names the format, so a picked file that happens to be JSON is refused with something readable.</summary>
	public const string FileKind = "mokaterm-settings";

	public const int CurrentVersion = 1;

	private const string KindProperty = "kind";
	private const string VersionProperty = "version";
	private const string ExportedAtProperty = "exportedAt";
	private const string SettingsProperty = "settings";

	private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

	private readonly SettingsService _settings;
	private readonly TimeProvider _timeProvider;
	private readonly IReadOnlyDictionary<string, SettingsSectionType> _sections;

	public SettingsBackup(SettingsService settings, TimeProvider timeProvider, IEnumerable<SettingsSectionType> sections)
	{
		_settings = settings;
		_timeProvider = timeProvider;
		_sections = sections
			.GroupBy(section => section.Key, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
	}

	public async Task<SettingsBackupFile> ExportAsync(CancellationToken cancellationToken = default)
	{
		JsonObject document = await _settings.CopyDocumentAsync(cancellationToken);
		foreach (KeyValuePair<string, JsonNode?> section in document)
		{
			if (section.Value is JsonObject values)
			{
				RemoveMachineValues(section.Key, values);
			}
		}

		DateTimeOffset now = _timeProvider.GetUtcNow();
		JsonObject file = new()
		{
			[KindProperty] = FileKind,
			[VersionProperty] = CurrentVersion,
			[ExportedAtProperty] = now,
			[SettingsProperty] = document,
		};

		return new SettingsBackupFile
		{
			FileName = string.Create(CultureInfo.InvariantCulture, $"mokaterm-settings-{now:yyyy-MM-dd}.json"),
			Content = JsonSerializer.SerializeToUtf8Bytes(file, MokatermJson.Document),
			Sections = document.Count,
		};
	}

	public SettingsBackupCheck Check(string fileName, byte[] content)
	{
		ParsedFile parsed = Parse(fileName, content);
		return new SettingsBackupCheck
		{
			Error = parsed.Error,
			ExportedAt = parsed.ExportedAt,
			Sections = parsed.Settings?.Count ?? 0,
		};
	}

	public async Task ImportAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
	{
		ParsedFile parsed = Parse(fileName, content);
		if (parsed.Settings is not { } imported)
		{
			throw new InvalidDataException(parsed.Error);
		}

		await _settings.ReplaceDocumentAsync(current => KeepMachineValues(imported, current), cancellationToken);
	}

	private ParsedFile Parse(string fileName, byte[] content)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
		ArgumentNullException.ThrowIfNull(content);

		// Editors on Windows like to start a UTF-8 file with a byte order mark, which the JSON reader refuses.
		ReadOnlySpan<byte> json = content.AsSpan();
		if (json.StartsWith(Utf8ByteOrderMark))
		{
			json = json[Utf8ByteOrderMark.Length..];
		}

		JsonNode? root;
		try
		{
			root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
			{
				CommentHandling = JsonCommentHandling.Skip,
				AllowTrailingCommas = true,
			});
		}
		catch (JsonException)
		{
			return ParsedFile.Failed($"{fileName} is not readable JSON.");
		}

		if (root is not JsonObject file
			|| !string.Equals(StringOf(file[KindProperty]), FileKind, StringComparison.Ordinal)
			|| file[SettingsProperty] is not JsonObject settings
			|| NumberOf(file[VersionProperty]) is not ({ } version and > 0))
		{
			return ParsedFile.Failed($"{fileName} is not a Mokaterm settings file.");
		}

		if (version > CurrentVersion)
		{
			return ParsedFile.Failed($"{fileName} comes from a newer version of Mokaterm.");
		}

		JsonObject sections = [];
		foreach (KeyValuePair<string, JsonNode?> section in settings)
		{
			// settings.json reads a section that is not an object as missing, so it is left out here as well.
			if (section.Value is not JsonObject values)
			{
				continue;
			}

			if (!_sections.TryGetValue(section.Key, out SettingsSectionType? type))
			{
				sections[section.Key] = values.DeepClone();
				continue;
			}

			if (Normalize(values, type.Type) is not { } normalized)
			{
				return ParsedFile.Failed($"The {section.Key} settings in {fileName} could not be read.");
			}

			sections[section.Key] = normalized;
		}

		return new ParsedFile(null, DateOf(file[ExportedAtProperty]), sections);
	}

	/// <summary>The section read through its type and the sanitizer and written back, or null when it does not fit the type.</summary>
	private static JsonNode? Normalize(JsonObject values, Type type)
	{
		try
		{
			object? section = values.Deserialize(type, MokatermJson.Document);
			return section is null
				? null
				: JsonSerializer.SerializeToNode(SettingsSanitizer.SanitizeSection(section), type, MokatermJson.Document);
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
		{
			return null;
		}
	}

	/// <summary>The imported sections with this machine's own values put back over whatever the file held for them.</summary>
	private JsonObject KeepMachineValues(JsonObject imported, JsonObject current)
	{
		JsonObject result = (JsonObject)imported.DeepClone();
		foreach (SettingsSectionType type in _sections.Values)
		{
			IReadOnlyList<string> names = MachineValueNames(type.Type);
			if (names.Count == 0)
			{
				continue;
			}

			JsonObject? section = result[type.Key] as JsonObject;
			if (section is not null)
			{
				RemoveNamed(section, names);
			}

			if (current[type.Key] is not JsonObject here)
			{
				continue;
			}

			foreach (KeyValuePair<string, JsonNode?> value in here.Where(pair => names.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)))
			{
				if (section is null)
				{
					section = [];
					result[type.Key] = section;
				}

				section[value.Key] = value.Value?.DeepClone();
			}
		}

		return result;
	}

	private void RemoveMachineValues(string key, JsonObject values)
	{
		if (_sections.TryGetValue(key, out SettingsSectionType? type))
		{
			RemoveNamed(values, MachineValueNames(type.Type));
		}
	}

	private static void RemoveNamed(JsonObject section, IReadOnlyList<string> names)
	{
		foreach (string name in section.Select(pair => pair.Key).Where(key => names.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList())
		{
			section.Remove(name);
		}
	}

	/// <summary>
	/// The JSON names of the section's <see cref="MachineSpecificAttribute"/> properties, from the serializer's own view of
	/// the type, so a renamed or camel-cased property is matched the way settings.json writes it. Compared without case,
	/// because the document is read that way and a hand edit may spell a name differently.
	/// </summary>
	private static IReadOnlyList<string> MachineValueNames(Type type) =>
		[.. MokatermJson.Document.GetTypeInfo(type).Properties
			.Where(IsMachineSpecific)
			.Select(property => property.Name)];

	private static bool IsMachineSpecific(JsonPropertyInfo property) =>
		property.AttributeProvider is MemberInfo member && member.IsDefined(typeof(MachineSpecificAttribute), inherit: true);

	private static string? StringOf(JsonNode? node) =>
		node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

	private static int? NumberOf(JsonNode? node) =>
		node is JsonValue value && value.TryGetValue(out int number) ? number : null;

	private static DateTimeOffset? DateOf(JsonNode? node) =>
		node is JsonValue value && value.TryGetValue(out DateTimeOffset date) ? date : null;

	private sealed record ParsedFile(string? Error, DateTimeOffset? ExportedAt, JsonObject? Settings)
	{
		public static ParsedFile Failed(string error) => new(error, null, null);
	}
}
