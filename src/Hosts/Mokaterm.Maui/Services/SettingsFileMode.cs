using System.Text.Json;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Maui.Services;

// The theme mode settings.json asks for, read before any window exists, so the window, WebView2's own background and the
// boot screen start in it instead of flashing dark. Only that one value is read, and with JsonDocument rather than the
// settings service, which would read and sanitize every section first. Anything unreadable is the default, dark.
internal static class SettingsFileMode
{
	private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

	private static readonly JsonDocumentOptions Options = new()
	{
		// The settings service reads the same file this leniently.
		CommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	public static bool ReadIsLight(string path)
	{
		try
		{
			return IsLight(File.ReadAllBytes(path));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	public static bool IsLight(ReadOnlyMemory<byte> json)
	{
		// A file saved from Notepad may start with a byte order mark, which JsonDocument takes from a stream but not from bytes.
		if (json.Span.StartsWith(Utf8Bom))
		{
			json = json[Utf8Bom.Length..];
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(json, Options);
			return Property(document.RootElement, AppearanceSettings.SectionKey) is { } appearance
				&& Property(appearance, nameof(AppearanceSettings.Mode)) is { } mode
				&& Read(mode) == ThemeMode.Light;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	// The service reads names without regard to case and takes an enum by its name or its number.
	private static JsonElement? Property(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		foreach (JsonProperty property in element.EnumerateObject())
		{
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				return property.Value;
			}
		}

		return null;
	}

	private static ThemeMode? Read(JsonElement mode) => mode.ValueKind switch
	{
		JsonValueKind.String when Enum.TryParse(mode.GetString(), ignoreCase: true, out ThemeMode named) => named,
		JsonValueKind.Number when mode.TryGetInt32(out int number) => (ThemeMode)number,
		_ => null,
	};
}
