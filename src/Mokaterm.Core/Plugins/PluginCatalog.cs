using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Core.Plugins;

/// <summary>
/// The plugins folder as one start of the app found it: changes waiting from the last run applied, every plugin
/// checked, and the ones that may run loaded. Hosts open it before their service container is built, because a plugin
/// registers services like a built-in module.
/// </summary>
public sealed class PluginCatalog
{
	private const int MaxThemeFiles = 64;

	// The same cap the theme import gives a pasted or picked scheme: half a scheme parses as a theme with half its colours.
	private const int MaxThemeBytes = 512 * 1024;

	private readonly bool _wroteMarker;

	private PluginCatalog(PluginCatalogOptions options, PluginStateStore? store, bool safeStart, bool wroteMarker)
	{
		Options = options;
		Store = store;
		IsSafeStart = safeStart;
		_wroteMarker = wroteMarker;
	}

	public PluginCatalogOptions Options { get; }

	/// <summary>The plugins this start loaded, ready for <see cref="PluginBuilderExtensions.AddPlugins"/>.</summary>
	public IReadOnlyList<LoadedPlugin> Loaded { get; private set; } = [];

	/// <summary>Every installed plugin and what this start did with it.</summary>
	public IReadOnlyList<PluginRecord> Records { get; private set; } = [];

	/// <summary>True when this start loaded nothing, because the last one died while loading or it was asked to.</summary>
	public bool IsSafeStart { get; }

	internal PluginStateStore? Store { get; }

	internal IReadOnlyList<PluginThemeFile> Themes { get; private set; } = [];

	/// <summary>Opens <see cref="PluginCatalogOptions.Directory"/>. Never throws for a plugin; a bad one is a record.</summary>
	public static PluginCatalog Open(PluginCatalogOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		string directory = options.Directory;
		if (options.UserManaged)
		{
			Directory.CreateDirectory(directory);
		}

		if (!Directory.Exists(directory))
		{
			return new PluginCatalog(options, null, options.SkipAll, wroteMarker: false);
		}

		PluginStateStore? store = options.UserManaged ? new PluginStateStore(directory) : null;
		PluginState state = store?.Load() ?? new PluginState();
		if (store is not null)
		{
			state = ApplyPending(directory, state, store);
		}

		string marker = Path.Combine(directory, PluginLayout.StartMarker);
		bool safeStart = options.SkipAll || File.Exists(marker);
		bool wroteMarker = !safeStart && TryWriteMarker(marker);

		PluginCatalog catalog = new(options, store, safeStart, wroteMarker);
		catalog.Evaluate(state);
		return catalog;
	}

	/// <summary>
	/// Says the app came up with its plugins: the next start loads them again. Hosts call it once the first page has
	/// rendered (desktop) or the server listens (web).
	/// </summary>
	public void ConfirmStarted()
	{
		if (_wroteMarker)
		{
			TryDelete(Path.Combine(Options.Directory, PluginLayout.StartMarker));
		}
	}

	/// <summary>
	/// Takes back a plugin that loaded but cannot be added, such as one whose module id Mokaterm or another plugin already
	/// registered. Its themes go with it, and the Plugins page shows it as failed with <paramref name="problem"/>.
	/// </summary>
	internal void Reject(string id, string problem)
	{
		Loaded = [.. Loaded.Where(plugin => plugin.Info.Id != id)];
		Themes = [.. Themes.Where(theme => theme.PluginId != id)];
		Records = [.. Records.Select(record => record.Id == id ? record with { Status = PluginStatus.Failed, Problem = problem } : record)];
	}

	private void Evaluate(PluginState state)
	{
		string hostName = PluginHosts.For(Options.Host);
		HashSet<string> assemblyNames = new(StringComparer.OrdinalIgnoreCase);
		List<LoadedPlugin> loaded = [];
		List<PluginRecord> records = [];
		List<PluginThemeFile> themes = [];
		foreach (string folder in Directory.EnumerateDirectories(Options.Directory).Order(StringComparer.OrdinalIgnoreCase))
		{
			string folderName = Path.GetFileName(folder);
			if (!PluginLayout.IsPluginFolder(folderName))
			{
				continue;
			}

			PluginManifest manifest;
			try
			{
				manifest = ReadManifest(folder);
				if (manifest.Id != folderName)
				{
					throw new InvalidDataException($"The folder is named {folderName}, but the plugin's id is {manifest.Id}. They have to match.");
				}
			}
			catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
			{
				records.Add(new PluginRecord
				{
					Id = folderName,
					Name = folderName,
					Version = "",
					Status = PluginStatus.Failed,
					Problem = ex is InvalidDataException ? ex.Message : $"Its plugin.json could not be read ({ex.GetType().Name}).",
				});
				continue;
			}

			bool enabled = !Options.UserManaged || state.Plugins.TryGetValue(manifest.Id, out PluginStateEntry? entry) && entry.Enabled;
			(PluginStatus status, string? problem) = Decide(folder, manifest, hostName, enabled, state);
			if (status == PluginStatus.Enabled && manifest.HasCode)
			{
				try
				{
					loaded.Add(PluginAssemblyLoader.Load(folder, manifest, assemblyNames));
				}
				catch (InvalidDataException ex)
				{
					(status, problem) = (PluginStatus.Failed, ex.Message);
				}
			}

			int themeCount = CountThemes(folder);
			if (status == PluginStatus.Enabled)
			{
				ReadThemes(folder, manifest.Id, themes);
			}

			records.Add(new PluginRecord
			{
				Id = manifest.Id,
				Name = manifest.Name,
				Version = manifest.Version,
				Publisher = manifest.Publisher,
				Description = manifest.Description,
				HasCode = manifest.HasCode,
				Hosts = manifest.Hosts,
				Status = status,
				Problem = problem,
				ThemeCount = themeCount,
				IsEnabled = enabled,
			});
		}

		Loaded = loaded;
		Records = records;
		Themes = themes;
	}

	private (PluginStatus Status, string? Problem) Decide(string folder, PluginManifest manifest, string hostName, bool enabled, PluginState state)
	{
		if (PluginCompatibility.Problem(manifest, hostName) is { } incompatible)
		{
			return (PluginStatus.Incompatible, incompatible);
		}

		if (!enabled)
		{
			return (PluginStatus.Disabled, null);
		}

		if (Options.UserManaged && manifest.HasCode)
		{
			string? trusted = state.Plugins.TryGetValue(manifest.Id, out PluginStateEntry? entry) ? entry.Trusted : null;
			if (trusted is null || !string.Equals(trusted, PluginHash.HashFolder(folder), StringComparison.Ordinal))
			{
				return (PluginStatus.Changed, "Its files changed since you turned it on, so it was not loaded. Check where the new files came from before trusting them.");
			}
		}

		return IsSafeStart
			? (PluginStatus.Skipped, "This start skipped every plugin.")
			: (PluginStatus.Enabled, null);
	}

	private static PluginManifest ReadManifest(string folder)
	{
		string path = Path.Combine(folder, PluginLayout.ManifestFile);
		if (!File.Exists(path))
		{
			throw new InvalidDataException("The folder has no plugin.json.");
		}

		if (new FileInfo(path).Length > PluginManifestReader.MaxBytes)
		{
			throw new InvalidDataException("Its plugin.json is too large to be a manifest.");
		}

		return PluginManifestReader.Read(File.ReadAllBytes(path));
	}

	private static int CountThemes(string folder)
	{
		string themes = Path.Combine(folder, PluginLayout.ThemesFolder);
		return Directory.Exists(themes) ? Directory.EnumerateFiles(themes).Count() : 0;
	}

	private static void ReadThemes(string folder, string pluginId, List<PluginThemeFile> themes)
	{
		string directory = Path.Combine(folder, PluginLayout.ThemesFolder);
		if (!Directory.Exists(directory))
		{
			return;
		}

		foreach (string file in Directory.EnumerateFiles(directory).Order(StringComparer.OrdinalIgnoreCase).Take(MaxThemeFiles))
		{
			try
			{
				if (new FileInfo(file).Length <= MaxThemeBytes)
				{
					themes.Add(new PluginThemeFile(pluginId, Path.GetFileName(file), File.ReadAllText(file)));
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				// One unreadable theme leaves the plugin's other themes and the plugin itself alone.
			}
		}
	}

	// Removals first, then installs: an update replaces the old folder, which nothing has loaded yet in this process.
	private static PluginState ApplyPending(string directory, PluginState state, PluginStateStore store)
	{
		bool changed = false;
		Dictionary<string, PluginStateEntry> plugins = new(state.Plugins, StringComparer.Ordinal);
		foreach (string id in state.Remove)
		{
			if (PluginManifestReader.IsValidId(id))
			{
				TryDeleteFolder(Path.Combine(directory, id));
				TryDeleteFolder(Path.Combine(directory, PluginLayout.PendingFolder, id));
			}

			plugins.Remove(id);
			changed = true;
		}

		string pending = Path.Combine(directory, PluginLayout.PendingFolder);
		if (Directory.Exists(pending))
		{
			foreach (string folder in Directory.EnumerateDirectories(pending))
			{
				string id = Path.GetFileName(folder);
				if (!PluginManifestReader.IsValidId(id))
				{
					// A half-written install (".id-guid") from a run that ended mid-copy.
					TryDeleteFolder(folder);
					continue;
				}

				string target = Path.Combine(directory, id);
				try
				{
					if (Directory.Exists(target))
					{
						Directory.Delete(target, recursive: true);
					}

					Directory.Move(folder, target);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					// Left where it is; the next start tries again, and the page still shows it as waiting.
				}
			}
		}

		// A plugin whose folder is gone has nothing left to be trusted or turned on.
		foreach (string id in plugins.Keys.ToList())
		{
			if (!Directory.Exists(Path.Combine(directory, id)) && !Directory.Exists(Path.Combine(pending, id)))
			{
				plugins.Remove(id);
				changed = true;
			}
		}

		if (!changed)
		{
			return state;
		}

		PluginState applied = new() { Plugins = plugins, Remove = [] };

		// Run on the pool: this happens while a host builds itself, possibly on a UI thread with a context to return to.
		Task.Run(() => store.SaveAsync(applied, CancellationToken.None)).GetAwaiter().GetResult();
		return applied;
	}

	private static bool TryWriteMarker(string marker)
	{
		try
		{
			File.WriteAllText(marker, DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// A read-only folder (an operator's, on the web host) simply goes without the safe start.
			return false;
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// The worst case is one safe start the user did not need.
		}
	}

	private static void TryDeleteFolder(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Tried again at the next start.
		}
	}
}
