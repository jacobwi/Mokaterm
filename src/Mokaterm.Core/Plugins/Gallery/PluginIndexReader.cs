using System.Text.Json;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Core.Plugins.Gallery;

/// <summary>
/// Reads the gallery's <c>index.json</c> after its signature was checked, holds every field to the limits the format
/// sets, and works out which versions this build offers. The list is signed by the gallery's own workflow, so a field
/// that breaks a rule is a broken list, and the whole list is refused rather than read in part.
/// </summary>
internal static class PluginIndexReader
{
	/// <summary>The largest <c>index.json</c> read.</summary>
	public const int MaxIndexBytes = 4 * 1024 * 1024;

	/// <summary>The newest format this build reads.</summary>
	public const int Format = 1;

	private const int MaxPlugins = 2000;
	private const int MaxVersions = 100;
	private const int MaxText = 1000;
	private const int MaxVersionText = 64;
	private const int MaxNames = 32;
	private const int MaxNameLength = 40;
	private const int Sha256HexLength = 64;
	private const int CommitHexLength = 40;

	/// <param name="json">The bytes the signature covered.</param>
	/// <param name="hostName">This host's name in <see cref="PluginHosts"/>, which decides what is offered.</param>
	/// <exception cref="PluginGalleryException">The list breaks a rule of the format, or is in a newer one.</exception>
	public static PluginGalleryIndex Read(byte[] json, string hostName)
	{
		ArgumentNullException.ThrowIfNull(json);
		if (json.Length > MaxIndexBytes)
		{
			throw new PluginGalleryException($"The plugin list is larger than {MaxIndexBytes / (1024 * 1024)} MB, so it was not read.");
		}

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16, AllowDuplicateProperties = false });
		}
		catch (JsonException ex)
		{
			throw new PluginGalleryException("The plugin list is not valid JSON, so it was not used.", ex);
		}

		using (document)
		{
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				throw Damaged("it is not a JSON object");
			}

			// The format comes first: a newer list may change every other field, and the answer to that is an update of
			// Mokaterm, not "damaged".
			if (!root.TryGetProperty("format", out JsonElement format)
				|| format.ValueKind != JsonValueKind.Number
				|| !format.TryGetInt32(out int number)
				|| number < 1)
			{
				throw Damaged("it does not say which format it is in");
			}

			if (number > Format)
			{
				throw new PluginGalleryException("The plugin list is in a newer format than this version of Mokaterm reads. Update Mokaterm to browse the gallery.");
			}

			DateTimeOffset generated = Date(root, "generated", "the list");
			JsonElement plugins = Array(root, "plugins", "the list", MaxPlugins);
			List<PluginGalleryEntry> entries = [];
			HashSet<string> ids = new(StringComparer.Ordinal);
			int position = 0;
			foreach (JsonElement plugin in plugins.EnumerateArray())
			{
				position++;
				PluginGalleryEntry entry = ReadPlugin(plugin, position, hostName);
				if (!ids.Add(entry.Id))
				{
					throw Damaged($"it lists {entry.Id} twice");
				}

				entries.Add(entry);
			}

			return new PluginGalleryIndex { Generated = generated, Plugins = entries };
		}
	}

	private static PluginGalleryEntry ReadPlugin(JsonElement plugin, int position, string hostName)
	{
		string where = $"plugin {position}";
		if (plugin.ValueKind != JsonValueKind.Object)
		{
			throw Damaged($"{where} is not an object");
		}

		string id = Text(plugin, "id", where, MaxText);
		if (!PluginManifestReader.IsValidId(id))
		{
			throw Damaged($"{where} has an id that is not allowed");
		}

		where = id;
		bool hasCode = Bool(plugin, "hasCode", where);
		List<string> hosts = Names(plugin, "hosts", where, allowEmpty: true);
		List<PluginGalleryVersion> versions = [];
		foreach (JsonElement version in Array(plugin, "versions", where, MaxVersions).EnumerateArray())
		{
			PluginGalleryVersion read = ReadVersion(version, where);
			if (versions.Exists(other => string.Equals(other.Version, read.Version, StringComparison.Ordinal)))
			{
				throw Damaged($"{where} lists version {read.Version} twice");
			}

			versions.Add(read);
		}

		string? hostProblem = PluginCompatibility.HostProblem(hosts, hostName);
		List<PluginGalleryVersion> offered = hostProblem is null
			? versions.FindAll(version => version.Withdrawn is null && PluginCompatibility.Loads(hasCode, version.Sdk))
			: [];

		return new PluginGalleryEntry
		{
			Id = id,
			Name = Text(plugin, "name", where, MaxText, allowEmpty: false),
			Publisher = Text(plugin, "publisher", where, MaxText),
			Description = Text(plugin, "description", where, MaxText, multiline: true),
			Categories = Names(plugin, "categories", where, allowEmpty: false),
			Source = Https(plugin, "source", where),
			License = Text(plugin, "license", where, MaxText),
			Verified = Bool(plugin, "verified", where),
			HasCode = hasCode,
			Hosts = hosts,
			Versions = versions,
			Offered = offered,
			Unavailable = offered.Count > 0 ? null : hostProblem ?? Unavailable(versions),
		};
	}

	private static PluginGalleryVersion ReadVersion(JsonElement version, string plugin)
	{
		if (version.ValueKind != JsonValueKind.Object)
		{
			throw Damaged($"{plugin} has a version that is not an object");
		}

		string number = Text(version, "version", $"a version of {plugin}", MaxVersionText, allowEmpty: false);
		string where = $"{plugin} {number}";
		string sdk = Text(version, "sdk", where, MaxVersionText, allowEmpty: false);
		if (!PluginCompatibility.TryParseSdk(sdk, out _))
		{
			throw Damaged($"{where} has an sdk that is not a version such as 0.2");
		}

		if (!version.TryGetProperty("size", out JsonElement sizeElement)
			|| sizeElement.ValueKind != JsonValueKind.Number
			|| !sizeElement.TryGetInt64(out long size)
			|| size < 1
			|| size > PluginPackage.MaxPackageBytes)
		{
			throw Damaged($"{where} has no size, or one over {PluginPackage.MaxPackageBytes / (1024 * 1024)} MB");
		}

		return new PluginGalleryVersion
		{
			Version = number,
			Sdk = sdk,
			Url = Https(version, "url", where),
			Sha256 = Hex(version, "sha256", where, Sha256HexLength),
			Size = size,
			Commit = Hex(version, "commit", where, CommitHexLength),
			Published = Date(version, "published", where),
			Withdrawn = Withdrawn(version, where),
		};
	}

	private static string Unavailable(List<PluginGalleryVersion> versions)
	{
		if (versions.Count == 0)
		{
			return "The gallery lists no version of it.";
		}

		return versions.TrueForAll(version => version.Withdrawn is not null)
			? "Every version of it was withdrawn."
			: $"No version of it is built for Mokaterm {PluginCompatibility.HostSdkText} yet.";
	}

	private static string? Withdrawn(JsonElement version, string where)
	{
		if (!version.TryGetProperty("withdrawn", out JsonElement value) || value.ValueKind == JsonValueKind.Null)
		{
			return null;
		}

		if (value.ValueKind != JsonValueKind.String)
		{
			throw Damaged($"{where} has a withdrawn reason that is not text");
		}

		string reason = Checked(value.GetString()!, "withdrawn", where, MaxText, multiline: true);
		return reason.Length == 0 ? "The gallery gives no reason." : reason;
	}

	private static string Text(JsonElement owner, string name, string where, int maxLength, bool allowEmpty = true, bool multiline = false)
	{
		if (!owner.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
		{
			throw Damaged($"{where} has no {name}");
		}

		string text = Checked(value.GetString()!, name, where, maxLength, multiline);
		return !allowEmpty && text.Length == 0 ? throw Damaged($"{where} has an empty {name}") : text;
	}

	private static string Checked(string value, string name, string where, int maxLength, bool multiline)
	{
		string text = value.Trim();
		if (text.Length > maxLength)
		{
			throw Damaged($"{where} has a {name} longer than {maxLength} characters");
		}

		// A description may keep its line breaks; nothing else that ends up on the page may hold control characters.
		if (text.Any(c => char.IsControl(c) && !(multiline && c is '\n' or '\r' or '\t')))
		{
			throw Damaged($"{where} has control characters in its {name}");
		}

		return text;
	}

	/// <summary>Lower-case names such as categories or hosts: letters, digits and dashes.</summary>
	private static List<string> Names(JsonElement owner, string name, string where, bool allowEmpty)
	{
		List<string> names = [];
		foreach (JsonElement item in Array(owner, name, where, MaxNames).EnumerateArray())
		{
			string value = item.ValueKind == JsonValueKind.String ? item.GetString()!.Trim() : "";
			if (value.Length is 0 or > MaxNameLength || !value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
			{
				throw Damaged($"{where} has {name} that are not lower-case names");
			}

			if (!names.Contains(value, StringComparer.Ordinal))
			{
				names.Add(value);
			}
		}

		return !allowEmpty && names.Count == 0 ? throw Damaged($"{where} has no {name}") : names;
	}

	private static JsonElement Array(JsonElement owner, string name, string where, int maxItems)
	{
		if (!owner.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
		{
			throw Damaged($"{where} has no {name}");
		}

		return value.GetArrayLength() > maxItems ? throw Damaged($"{where} has more than {maxItems} {name}") : value;
	}

	private static bool Bool(JsonElement owner, string name, string where) =>
		owner.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: throw Damaged($"{where} has no {name}");

	private static Uri Https(JsonElement owner, string name, string where)
	{
		string text = Text(owner, name, where, MaxText, allowEmpty: false);
		return Uri.TryCreate(text, UriKind.Absolute, out Uri? address) && address.Scheme == Uri.UriSchemeHttps && address.Host.Length > 0
			? address
			: throw Damaged($"{where} has a {name} that is not an https address");
	}

	private static string Hex(JsonElement owner, string name, string where, int length)
	{
		string text = Text(owner, name, where, length);
		return text.Length == length && text.All(char.IsAsciiHexDigitLower)
			? text
			: throw Damaged($"{where} has a {name} that is not {length} lower-case hex characters");
	}

	private static DateTimeOffset Date(JsonElement owner, string name, string where) =>
		owner.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out DateTimeOffset date)
			? date
			: throw Damaged($"{where} has no {name} time");

	private static PluginGalleryException Damaged(string detail) =>
		new($"The plugin list is damaged ({detail}), so it was not used.");
}
