using System.Text.Json;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.Core.Serialization;

namespace Mokaterm.Core.Plugins;

/// <summary>
/// Reads and checks <c>plugin.json</c>. Everything in it ends up on the Plugins page, in a folder name or in a path the
/// page requests, so every field is held to a shape that is safe in all three.
/// </summary>
internal static class PluginManifestReader
{
	/// <summary>Far more than any real manifest; a bigger one is not a manifest.</summary>
	public const int MaxBytes = 64 * 1024;

	private const int MaxIdLength = 64;
	private const int MaxShortText = 100;
	private const int MaxDescription = 1000;

	/// <exception cref="InvalidDataException">The manifest is missing a field or holds one that is not allowed.</exception>
	public static PluginManifest Read(ReadOnlySpan<byte> json)
	{
		if (json.Length > MaxBytes)
		{
			throw new InvalidDataException("The plugin's plugin.json is too large to be a manifest.");
		}

		RawManifest raw;
		try
		{
			raw = JsonSerializer.Deserialize<RawManifest>(json, MokatermJson.Compact)
				?? throw new InvalidDataException("The plugin's plugin.json is empty.");
		}
		catch (JsonException)
		{
			throw new InvalidDataException("The plugin's plugin.json is not valid JSON.");
		}

		string id = Required(raw.Id, "id");
		if (!IsValidId(id))
		{
			throw new InvalidDataException(
				$"The plugin id \"{Shorten(id)}\" is not allowed: use lower-case letters and digits, joined by dots or dashes, up to {MaxIdLength} characters.");
		}

		string? assembly = Optional(raw.Assembly, "assembly", MaxShortText);
		if (assembly is not null
			&& (Path.GetFileName(assembly) != assembly
				|| !assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
				|| assembly.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
		{
			throw new InvalidDataException("The plugin's assembly must be a .dll file name, without a folder.");
		}

		string? sdk = Optional(raw.Sdk, "sdk", 16);
		if (sdk is not null && !PluginCompatibility.TryParseSdk(sdk, out _))
		{
			throw new InvalidDataException($"The plugin's sdk \"{sdk}\" is not a version such as 0.2.");
		}

		if (assembly is not null && sdk is null)
		{
			throw new InvalidDataException("The plugin carries code but does not say which Mokaterm it was built for (sdk).");
		}

		return new PluginManifest
		{
			Id = id,
			Name = Text(Required(raw.Name, "name"), "name", MaxShortText),
			Version = Text(Required(raw.Version, "version"), "version", 64),
			Publisher = Optional(raw.Publisher, "publisher", MaxShortText),
			Description = Optional(raw.Description, "description", MaxDescription),
			Sdk = sdk,
			Assembly = assembly,
			Hosts = ReadHosts(raw.Hosts),
		};
	}

	public static bool IsValidId(string id)
	{
		if (id.Length is 0 or > MaxIdLength || !char.IsAsciiLetterLower(id[0]) && !char.IsAsciiDigit(id[0]))
		{
			return false;
		}

		char previous = id[0];
		foreach (char c in id.AsSpan(1))
		{
			bool separator = c is '.' or '-';
			if (!separator && !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) || separator && previous is '.' or '-')
			{
				return false;
			}

			previous = c;
		}

		return previous is not ('.' or '-');
	}

	private static List<string> ReadHosts(string[]? hosts)
	{
		List<string> result = [];
		foreach (string host in hosts ?? [])
		{
			string name = host.Trim().ToLowerInvariant();
			if (name is not (PluginHosts.Desktop or PluginHosts.Web))
			{
				throw new InvalidDataException($"The plugin names a host Mokaterm does not know: \"{Shorten(host)}\".");
			}

			if (!result.Contains(name))
			{
				result.Add(name);
			}
		}

		return result;
	}

	private static string Required(string? value, string field) =>
		string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException($"The plugin's plugin.json has no {field}.") : value.Trim();

	private static string? Optional(string? value, string field, int maxLength) =>
		string.IsNullOrWhiteSpace(value) ? null : Text(value.Trim(), field, maxLength);

	private static string Text(string value, string field, int maxLength)
	{
		if (value.Length > maxLength)
		{
			throw new InvalidDataException($"The plugin's {field} is longer than {maxLength} characters.");
		}

		if (value.Any(char.IsControl) && field != "description")
		{
			throw new InvalidDataException($"The plugin's {field} holds control characters.");
		}

		return value;
	}

	private static string Shorten(string value) => value.Length <= 40 ? value : value[..40] + "...";

	private sealed record RawManifest
	{
		public string? Id { get; init; }

		public string? Name { get; init; }

		public string? Version { get; init; }

		public string? Publisher { get; init; }

		public string? Description { get; init; }

		public string? Sdk { get; init; }

		public string? Assembly { get; init; }

		public string[]? Hosts { get; init; }
	}
}
