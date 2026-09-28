using System.Collections.Frozen;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Mokaterm.Abstractions.Import;

namespace Mokaterm.Core.Import;

/// <summary>
/// Reads <c>~/.ssh/config</c>, or a config file the user picks. Concrete <c>Host</c> aliases become logins;
/// wildcard blocks only hand their settings to the aliases they match.
/// </summary>
internal sealed class OpenSshConfigImportSource : IConnectionImportSource
{
	public const string SourceId = "openssh-config";

	/// <summary>A config far past this is not one, and includes should not be able to grow it without bound.</summary>
	private const int MaxFileBytes = 2 * 1024 * 1024;

	private const int DefaultSshPort = 22;

	/// <summary>
	/// Patterns and character comparisons allowed for matching hosts against wildcard blocks. Thousands of hosts under
	/// a few dozen wildcard blocks use a few percent of it; spending all of it takes a fraction of a second.
	/// </summary>
	private const long MaxPatternWork = 40_000_000;

	/// <summary>The keywords an entry is built from; <see cref="BuildEntries"/> ignores every other setting.</summary>
	private static readonly FrozenSet<string> UsedKeywords =
		new[] { "hostname", "port", "identityfile", "user", "proxyjump", "proxycommand" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	private readonly Func<string> _homeDirectory;

	public OpenSshConfigImportSource()
		: this(() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
	{
	}

	internal OpenSshConfigImportSource(Func<string> homeDirectory) => _homeDirectory = homeDirectory;

	public ImportSourceInfo Info { get; } = new()
	{
		Id = SourceId,
		DisplayName = "OpenSSH",
		Description = "Host entries in the ssh config file",
		Kind = ImportSourceKind.Installed,
		AcceptsFile = true,
	};

	public ValueTask<ImportPreview> ReadAsync(ImportReadRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult(request.File is { } file ? ReadFile(file) : ReadDefault());
	}

	private static ImportPreview ReadText(string text, string location, string? directory)
	{
		Func<string, IReadOnlyList<OpenSshConfigFile>>? resolve =
			directory is null ? null : argument => ResolveInclude(argument, directory);

		OpenSshConfigParseResult parsed = OpenSshConfigParser.Parse(text, resolve);
		List<ImportSkip> skipped = [.. parsed.Notes];
		List<ImportedEntry> entries = BuildEntries(parsed, skipped);
		return entries.Count == 0
			? ImportPreview.Nothing(SourceId, "The config has no host entries to import.", location) with { Skipped = skipped }
			: new ImportPreview
			{
				SourceId = SourceId,
				Availability = ImportAvailability.Found,
				Location = location,
				Entries = entries,
				Skipped = skipped,
			};
	}

	private static List<ImportedEntry> BuildEntries(OpenSshConfigParseResult parsed, List<ImportSkip> skipped)
	{
		IReadOnlyList<OpenSshConfigBlock> blocks = parsed.Blocks;

		// One alias per folded name: two spellings the matcher cannot tell apart are one host to OpenSSH as well.
		List<(string Alias, string Key)> aliases = [];
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (OpenSshConfigBlock block in blocks)
		{
			foreach (string pattern in block.Patterns)
			{
				if (OpenSshHostPattern.IsWildcard(pattern))
				{
					continue;
				}

				string key = OpenSshHostPattern.Fold(pattern);
				if (seen.Add(key))
				{
					aliases.Add((pattern, key));
				}
			}
		}

		// Checking every alias against every block is quadratic, which a crafted config of a megabyte or two turns into
		// minutes on the thread that opened the wizard. Blocks that only name hosts are found by name instead, the rest
		// are matched against a budget, and each block brings only the few settings an entry is built from.
		KeyValuePair<string, string>[][] used = new KeyValuePair<string, string>[blocks.Count][];
		Dictionary<string, List<int>> named = new(StringComparer.Ordinal);
		List<int> patterned = [];
		for (int index = 0; index < blocks.Count; index++)
		{
			used[index] = UsedSettings(blocks[index]);
			if (blocks[index].Patterns.Any(OpenSshHostPattern.IsWildcard))
			{
				patterned.Add(index);
				continue;
			}

			foreach (string pattern in blocks[index].Patterns)
			{
				string key = OpenSshHostPattern.Fold(pattern);
				if (!named.TryGetValue(key, out List<int>? naming))
				{
					named[key] = naming = [];
				}

				if (naming.Count == 0 || naming[^1] != index)
				{
					naming.Add(index);
				}
			}
		}

		List<ImportedEntry> entries = [];
		long budget = MaxPatternWork;
		for (int aliasIndex = 0; aliasIndex < aliases.Count; aliasIndex++)
		{
			string alias = aliases[aliasIndex].Alias;
			ReadOnlySpan<int> direct = named.TryGetValue(aliases[aliasIndex].Key, out List<int>? naming) ? CollectionsMarshal.AsSpan(naming) : [];
			Dictionary<string, string> settings = new(StringComparer.OrdinalIgnoreCase);
			int nextDirect = 0;
			int nextPatterned = 0;
			while (nextDirect < direct.Length || nextPatterned < patterned.Count)
			{
				// Both lists are in file order, and the order is what decides which value of a keyword wins.
				int index;
				if (nextPatterned >= patterned.Count || (nextDirect < direct.Length && direct[nextDirect] < patterned[nextPatterned]))
				{
					index = direct[nextDirect++];
				}
				else
				{
					index = patterned[nextPatterned++];
					bool covers = OpenSshHostPattern.Covers(blocks[index].Patterns, alias, ref budget);
					if (budget <= 0)
					{
						skipped.Add(new ImportSkip(
							alias,
							$"This host and the {aliases.Count - aliasIndex - 1} after it were left out: the config has more wildcard patterns than the import can match against every host."));
						return entries;
					}

					if (!covers)
					{
						continue;
					}
				}

				foreach (KeyValuePair<string, string> setting in used[index])
				{
					// OpenSSH keeps the first value it reads for a keyword, so later blocks cannot overwrite it.
					settings.TryAdd(setting.Key, setting.Value);
				}
			}

			string address = settings.TryGetValue("hostname", out string? hostName) && hostName.Length > 0
				? hostName.Replace("%h", alias, StringComparison.Ordinal)
				: alias;

			int? port = settings.TryGetValue("port", out string? portText)
				&& int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort)
					? ImportPorts.Normalize(parsedPort, DefaultSshPort)
					: null;

			string? identity = settings.TryGetValue("identityfile", out string? identityFile)
				? FirstArgument(identityFile)
				: null;

			entries.Add(new ImportedEntry
			{
				Key = SourceId + ":" + alias,
				Name = alias,
				Address = address,
				ProtocolId = ImportProtocols.Ssh,
				Port = port,
				Username = settings.TryGetValue("user", out string? user) ? user : null,
				IdentityFilePath = identity,
				Notes = BuildNotes(settings),
			});
		}

		if (entries.Count == 0 && parsed.Blocks.Count > 0)
		{
			skipped.Add(new ImportSkip("Host *", "Only wildcard blocks were found, and those are settings rather than hosts."));
		}

		return entries;
	}

	/// <summary>The first value of each keyword in <see cref="UsedKeywords"/>, in the order the block has them.</summary>
	private static KeyValuePair<string, string>[] UsedSettings(OpenSshConfigBlock block)
	{
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		List<KeyValuePair<string, string>> used = [];
		foreach (KeyValuePair<string, string> setting in block.Settings)
		{
			if (UsedKeywords.Contains(setting.Key) && seen.Add(setting.Key))
			{
				used.Add(setting);
			}
		}

		return [.. used];
	}

	private static string? BuildNotes(Dictionary<string, string> settings)
	{
		StringBuilder builder = new();
		if (settings.TryGetValue("proxyjump", out string? jump) && jump.Length > 0)
		{
			builder.Append("Reached through ").Append(jump).Append('.');
		}

		if (settings.TryGetValue("proxycommand", out string? command) && command.Length > 0)
		{
			Separate(builder).Append("ProxyCommand: ").Append(command);
		}

		return builder.Length == 0 ? null : builder.ToString();
	}

	private static StringBuilder Separate(StringBuilder builder) =>
		builder.Length == 0 ? builder : builder.Append(' ');

	private static string? FirstArgument(string value)
	{
		string trimmed = value.Trim();
		int space = trimmed.IndexOf(' ', StringComparison.Ordinal);
		string first = space < 0 ? trimmed : trimmed[..space];
		return first.Length == 0 ? null : first;
	}

	/// <summary>
	/// Turns an <c>Include</c> argument into files. Relative paths start at the config's own directory, which is
	/// where OpenSSH looks for a user config, and a plain <c>*</c> glob is expanded.
	/// </summary>
	private static List<OpenSshConfigFile> ResolveInclude(string argument, string directory)
	{
		string pattern = argument.Trim().Trim('"');
		if (pattern.Length == 0)
		{
			return [];
		}

		if (pattern.StartsWith('~'))
		{
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			pattern = Path.Combine(home, pattern.TrimStart('~').TrimStart('/', '\\'));
		}

		string full = Path.IsPathRooted(pattern) ? pattern : Path.Combine(directory, pattern);
		string folder = Path.GetDirectoryName(full) ?? directory;
		string leaf = Path.GetFileName(full);
		if (leaf.Length == 0 || !Directory.Exists(folder))
		{
			return [];
		}

		List<OpenSshConfigFile> files = [];
		try
		{
			string[] matches = leaf.Contains('*', StringComparison.Ordinal) || leaf.Contains('?', StringComparison.Ordinal)
				? Directory.GetFiles(folder, leaf)
				: File.Exists(full) ? [full] : [];

			Array.Sort(matches, StringComparer.OrdinalIgnoreCase);
			foreach (string path in matches)
			{
				if (TryReadAllText(path, out string text))
				{
					files.Add(new OpenSshConfigFile(path, text));
				}
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
		{
			return files;
		}

		return files;
	}

	private static bool TryReadAllText(string path, out string text)
	{
		text = "";
		try
		{
			FileInfo info = new(path);
			if (!info.Exists || info.Length > MaxFileBytes)
			{
				return false;
			}

			text = File.ReadAllText(path);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
		{
			return false;
		}
	}

	private ImportPreview ReadDefault()
	{
		string directory = Path.Combine(_homeDirectory(), ".ssh");
		string path = Path.Combine(directory, "config");
		if (!File.Exists(path))
		{
			return ImportPreview.Nothing(SourceId, "No ssh config file was found. Pick one to import from somewhere else.", path);
		}

		return !TryReadAllText(path, out string text)
			? ImportPreview.Failed(SourceId, "The ssh config file could not be read.", path)
			: ReadText(text, path, directory);
	}

	private static ImportPreview ReadFile(ImportFile file)
	{
		if (file.Content.Length > MaxFileBytes)
		{
			return ImportPreview.Failed(SourceId, "This file is too large to be an ssh config.", file.Path ?? file.Name);
		}

		string text = DecodeText(file.Content);
		string? directory = file.Path is { Length: > 0 } path ? Path.GetDirectoryName(path) : null;
		return ReadText(text, file.Path ?? file.Name, directory);
	}

	private static string DecodeText(byte[] content) =>
		new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(content).TrimStart('﻿');
}
