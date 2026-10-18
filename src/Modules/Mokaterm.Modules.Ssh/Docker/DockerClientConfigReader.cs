using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Reads what <see cref="DockerRegistryCommands.ReadClientConfig"/> printed: the account, the file, then the file itself
/// with its credential values already blanked by the server where its sed could. Only keys and helper names are taken;
/// a credential value is never turned into text, only told apart from an empty one, and the bytes are wiped once read
/// whatever happened, so nothing of a credential that still came across outlives the read.
/// </summary>
internal static partial class DockerClientConfigReader
{
	/// <summary>The line in front of the file's bytes.</summary>
	public const string Marker = "@@mt:config";

	/// <summary>The largest file the script sends; larger is refused there, before anything is read.</summary>
	public const int MaxFileBytes = 1024 * 1024;

	/// <summary>The most registries kept from one file: past this it is not a config anybody wrote by hand or by login.</summary>
	public const int MaxRegistries = 1000;

	private const int MaxKeyLength = 512;

	private static ReadOnlySpan<byte> MarkerLine => "@@mt:config\n"u8;

	// The fields of an auths entry that hold a credential. docker writes auth and identitytoken; the rest are what a
	// hand-written file may carry.
	private static readonly string[] SecretFields = ["auth", "identitytoken", "registrytoken", "password"];

	/// <summary>Reads the output and wipes <see cref="DockerOutput.RawOutput"/>, read or not.</summary>
	/// <exception cref="DockerCommandException">The output is cut short, or the file is not JSON.</exception>
	public static DockerClientConfig Read(DockerOutput output)
	{
		ArgumentNullException.ThrowIfNull(output);
		Memory<byte> bytes = MemoryMarshal.AsMemory(output.RawOutput);
		try
		{
			return Parse(bytes.Span, output.OutputTruncated);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(bytes.Span);
		}
	}

	private static DockerClientConfig Parse(ReadOnlySpan<byte> bytes, bool truncated)
	{
		int first = bytes.IndexOf((byte)'\n');
		int second = first < 0 ? -1 : bytes[(first + 1)..].IndexOf((byte)'\n');
		if (second < 0)
		{
			throw new DockerCommandException(DockerFailure.UnreadableOutput, "The client configuration could not be read: the script printed nothing this app can read.");
		}

		string user = Plain(bytes[..first]);
		ReadOnlySpan<byte> rest = bytes[(first + 1)..];
		string path = Plain(rest[..second]);
		rest = rest[(second + 1)..];

		ReadOnlySpan<byte> marker = MarkerLine;
		if (!rest.StartsWith(marker))
		{
			return new DockerClientConfig { User = user, Path = path };
		}

		if (truncated)
		{
			throw new DockerCommandException(DockerFailure.UnreadableOutput, $"{path} came across cut short, so the registries in it cannot be listed.");
		}

		try
		{
			return ParseJson(rest[marker.Length..], user, path);
		}
		catch (JsonException)
		{
			// Not passed on: the reader's own message quotes the bytes it stopped at, which can be part of a credential.
			throw new DockerCommandException(DockerFailure.UnreadableOutput, $"{path} is not valid JSON, so the registries in it cannot be listed.");
		}
	}

	private static DockerClientConfig ParseJson(ReadOnlySpan<byte> json, string user, string path)
	{
		Utf8JsonReader reader = new(json, new JsonReaderOptions
		{
			CommentHandling = JsonCommentHandling.Skip,
			AllowTrailingCommas = true,
			MaxDepth = 32,
		});

		if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
		{
			throw new DockerCommandException(DockerFailure.UnreadableOutput, $"{path} does not hold a JSON object, so the registries in it cannot be listed.");
		}

		List<Builder> entries = [];
		string? store = null;
		while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
		{
			bool auths = reader.ValueTextEquals("auths");
			bool helpers = !auths && reader.ValueTextEquals("credHelpers");
			bool credentialsStore = !auths && !helpers && reader.ValueTextEquals("credsStore");
			_ = reader.Read();
			if (auths && reader.TokenType == JsonTokenType.StartObject)
			{
				ReadAuths(ref reader, entries);
			}
			else if (helpers && reader.TokenType == JsonTokenType.StartObject)
			{
				ReadHelpers(ref reader, entries);
			}
			else if (credentialsStore && reader.TokenType == JsonTokenType.String)
			{
				store = HelperName(reader.GetString());
			}
			else
			{
				reader.Skip();
			}
		}

		return new DockerClientConfig
		{
			User = user,
			Path = path,
			Exists = true,
			CredentialsStore = store,
			Registries = [.. entries.Select(entry => entry.Build())],
		};
	}

	private static void ReadAuths(ref Utf8JsonReader reader, List<Builder> entries)
	{
		while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
		{
			string? key = Key(reader.GetString());
			_ = reader.Read();
			bool stored = false;
			if (reader.TokenType == JsonTokenType.StartObject)
			{
				while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
				{
					bool secret = IsSecretField(ref reader);
					_ = reader.Read();
					if (secret && reader.TokenType == JsonTokenType.String)
					{
						// Only whether it is empty: the value itself is never read into text.
						stored |= reader.ValueSpan.Length > 0;
					}
					else
					{
						reader.Skip();
					}
				}
			}
			else
			{
				reader.Skip();
			}

			if (key is not null && Entry(entries, key) is { } entry)
			{
				entry.LoggedIn = true;
				entry.StoredInFile |= stored;
			}
		}
	}

	private static void ReadHelpers(ref Utf8JsonReader reader, List<Builder> entries)
	{
		while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
		{
			string? key = Key(reader.GetString());
			_ = reader.Read();
			string? helper = reader.TokenType == JsonTokenType.String ? HelperName(reader.GetString()) : null;
			reader.Skip();
			if (key is not null && helper is not null && Entry(entries, key) is { } entry)
			{
				entry.Helper = helper;
			}
		}
	}

	private static bool IsSecretField(ref Utf8JsonReader reader)
	{
		foreach (string field in SecretFields)
		{
			if (reader.ValueTextEquals(field))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>The entry for a key, made when it is new; null past <see cref="MaxRegistries"/>.</summary>
	private static Builder? Entry(List<Builder> entries, string key)
	{
		if (entries.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.Ordinal)) is { } found)
		{
			return found;
		}

		if (entries.Count >= MaxRegistries)
		{
			return null;
		}

		Builder made = new(key);
		entries.Add(made);
		return made;
	}

	/// <summary>A key a list can show and a logout can be given; anything with a control character is left out.</summary>
	private static string? Key(string? key) =>
		key is { Length: > 0 and <= MaxKeyLength } && !key.Any(char.IsControl) ? key : null;

	/// <summary>A helper's name as <c>docker-credential-NAME</c> takes it, or null for anything else.</summary>
	private static string? HelperName(string? name) => name is not null && HelperRule().IsMatch(name) ? name : null;

	private static string Plain(ReadOnlySpan<byte> line) => Encoding.UTF8.GetString(line).TrimEnd('\r').Trim();

	[GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant)]
	private static partial Regex HelperRule();

	private sealed class Builder(string key)
	{
		public string Key { get; } = key;

		public bool LoggedIn { get; set; }

		public bool StoredInFile { get; set; }

		public string? Helper { get; set; }

		public DockerRegistryEntry Build() => new() { Key = Key, LoggedIn = LoggedIn, StoredInFile = StoredInFile, Helper = Helper };
	}
}
