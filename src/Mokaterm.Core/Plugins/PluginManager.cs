using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Core.Plugins;

/// <summary>
/// What the Plugins page reads and changes. Changes go to <c>plugins.json</c> and the pending folder and take effect
/// at the next start, so the list is this start's records with the waiting change on each.
/// </summary>
internal sealed class PluginManager : IPluginManager, IDisposable
{
	private readonly PluginCatalog _catalog;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private PluginState _state;
	private IReadOnlyList<PluginRecord> _plugins = [];

	public PluginManager(PluginCatalog catalog)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		_catalog = catalog;
		_state = catalog.Store?.Load() ?? new PluginState();
		Rebuild();
	}

	public event EventHandler? Changed;

	public string Directory => _catalog.Options.Directory;

	public bool CanInstall => _catalog.Options.UserManaged;

	public bool IsSafeStart => _catalog.IsSafeStart;

	public bool RestartRequired => _plugins.Any(plugin => plugin.Pending != PluginPendingChange.None);

	public IReadOnlyList<PluginRecord> Plugins => _plugins;

	private string PendingDirectory => Path.Combine(Directory, PluginLayout.PendingFolder);

	public void Dispose() => _gate.Dispose();

	public async Task<IPluginPackage> ReadPackageAsync(Stream package, PluginPackageOrigin? origin, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(package);
		using MemoryStream copy = new();
		byte[] buffer = new byte[81920];
		int read;
		while ((read = await package.ReadAsync(buffer, cancellationToken)) > 0)
		{
			if (copy.Length + read > PluginPackage.MaxPackageBytes)
			{
				throw new InvalidDataException($"The file is larger than {PluginPackage.MaxPackageBytes / (1024 * 1024)} MB, which no plugin should be.");
			}

			copy.Write(buffer, 0, read);
		}

		string hostName = PluginHosts.For(_catalog.Options.Host);
		return PluginPackage.Read(copy.ToArray(), InstalledVersion, hostName, origin);
	}

	public async Task InstallAsync(IPluginPackage package, bool enable, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(package);
		EnsureUserManaged();
		if (package is not PluginPackage read)
		{
			throw new ArgumentException("The package was not read by this manager.", nameof(package));
		}

		if (read.Problem is { } problem)
		{
			throw new InvalidOperationException(problem);
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			// Written beside its final place and renamed, so a run that ends mid-copy leaves a dotted folder the next
			// start throws away, never a half-written install.
			System.IO.Directory.CreateDirectory(PendingDirectory);
			string staging = Path.Combine(PendingDirectory, $".{read.Id}-{Guid.NewGuid():N}");
			string target = Path.Combine(PendingDirectory, read.Id);
			try
			{
				read.ExtractTo(staging);
				if (System.IO.Directory.Exists(target))
				{
					System.IO.Directory.Delete(target, recursive: true);
				}

				System.IO.Directory.Move(staging, target);
			}
			catch
			{
				TryDeleteFolder(staging);
				throw;
			}

			PluginStateEntry entry = new() { Enabled = enable, Trusted = enable && read.HasCode ? read.Hash : null };
			await SaveAsync(_state with
			{
				Plugins = new(_state.Plugins, StringComparer.Ordinal) { [read.Id] = entry },
				Remove = [.. _state.Remove.Where(id => id != read.Id)],
			}, cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	public Task<PluginFileSet> ReadFilesAsync(string id, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		string folder = NextStartFolder(id) ?? throw new InvalidOperationException($"No plugin named {id} is installed.");

		// Hashing reads every file; off the caller's thread, which is a page's.
		return Task.Run(() =>
		{
			List<PluginPackageFile> files = PluginHash.ListFolder(folder);
			files.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
			return new PluginFileSet(files, PluginHash.Combine(files));
		}, cancellationToken);
	}

	public async Task SetEnabledAsync(string id, bool enabled, string? trustedHash, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		EnsureUserManaged();
		await _gate.WaitAsync(cancellationToken);
		try
		{
			string folder = NextStartFolder(id) ?? throw new InvalidOperationException($"No plugin named {id} is installed.");
			bool hasCode = ReadManifest(folder)?.HasCode ?? false;

			// Trusting means the files the user was shown: the next start loads only if they still match.
			string? trusted = null;
			if (enabled && hasCode)
			{
				string current = PluginHash.HashFolder(folder);
				if (!string.Equals(current, trustedHash, StringComparison.Ordinal))
				{
					throw new InvalidDataException("The plugin's files changed while you were looking at them. Look at them again before trusting them.");
				}

				trusted = current;
			}
			await SaveAsync(_state with
			{
				Plugins = new(_state.Plugins, StringComparer.Ordinal) { [id] = new PluginStateEntry { Enabled = enabled, Trusted = trusted } },
			}, cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task RemoveAsync(string id, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		EnsureUserManaged();
		await _gate.WaitAsync(cancellationToken);
		try
		{
			// A waiting install goes now: nothing has loaded from the pending folder.
			TryDeleteFolder(Path.Combine(PendingDirectory, id));
			bool installed = _catalog.Records.Any(record => record.Id == id);
			Dictionary<string, PluginStateEntry> plugins = new(_state.Plugins, StringComparer.Ordinal);
			if (!installed)
			{
				plugins.Remove(id);
			}

			await SaveAsync(_state with
			{
				Plugins = plugins,
				Remove = installed ? [.. _state.Remove.Where(other => other != id), id] : [.. _state.Remove.Where(other => other != id)],
			}, cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task SaveAsync(PluginState state, CancellationToken cancellationToken)
	{
		PluginStateStore store = _catalog.Store ?? throw new InvalidOperationException("Plugins are not managed through the app here.");
		await store.SaveAsync(state, cancellationToken);
		_state = state;
		Rebuild();
		Changed?.Invoke(this, EventArgs.Empty);
	}

	private void Rebuild()
	{
		List<PluginRecord> plugins = [];
		foreach (PluginRecord record in _catalog.Records)
		{
			PluginPendingChange pending = PendingFor(record);
			plugins.Add(record with
			{
				IsEnabled = CanInstall ? _state.Plugins.TryGetValue(record.Id, out PluginStateEntry? entry) && entry.Enabled : record.IsEnabled,
				Pending = pending,
				PendingVersion = pending is PluginPendingChange.Install or PluginPendingChange.Update
					? PendingFolderVersion(record.Id)
					: null,
			});
		}

		// Installs of plugins this start does not have yet.
		if (CanInstall && System.IO.Directory.Exists(PendingDirectory))
		{
			foreach (string folder in System.IO.Directory.EnumerateDirectories(PendingDirectory).Order(StringComparer.OrdinalIgnoreCase))
			{
				string id = Path.GetFileName(folder);
				if (!PluginManifestReader.IsValidId(id) || plugins.Exists(plugin => plugin.Id == id) || ReadManifest(folder) is not { } manifest)
				{
					continue;
				}

				plugins.Add(new PluginRecord
				{
					Id = manifest.Id,
					Name = manifest.Name,
					Version = manifest.Version,
					Publisher = manifest.Publisher,
					Description = manifest.Description,
					HasCode = manifest.HasCode,
					Hosts = manifest.Hosts,
					Status = PluginStatus.Disabled,
					IsEnabled = _state.Plugins.TryGetValue(id, out PluginStateEntry? entry) && entry.Enabled,
					Pending = PluginPendingChange.Install,
					PendingVersion = manifest.Version,
				});
			}
		}

		_plugins = plugins;
	}

	private PluginPendingChange PendingFor(PluginRecord record)
	{
		if (!CanInstall)
		{
			return PluginPendingChange.None;
		}

		if (_state.Remove.Contains(record.Id))
		{
			return PluginPendingChange.Remove;
		}

		if (System.IO.Directory.Exists(Path.Combine(PendingDirectory, record.Id)))
		{
			return PluginPendingChange.Update;
		}

		bool enabledNext = _state.Plugins.TryGetValue(record.Id, out PluginStateEntry? entry) && entry.Enabled;
		return (enabledNext, record.IsEnabled) switch
		{
			(true, false) => PluginPendingChange.Enable,
			(false, true) => PluginPendingChange.Disable,

			// Trusting new files for a plugin that was refused this start also waits for a restart.
			(true, true) when record.Status == PluginStatus.Changed && entry?.Trusted is not null => PluginPendingChange.Enable,
			_ => PluginPendingChange.None,
		};
	}

	/// <summary>The version waiting in the pending folder, or null when no folder or manifest is there to read.</summary>
	private string? PendingFolderVersion(string id) =>
		PluginManifestReader.IsValidId(id) && ReadManifest(Path.Combine(PendingDirectory, id)) is { } manifest
			? manifest.Version
			: null;

	private string? InstalledVersion(string id) =>
		_plugins.FirstOrDefault(plugin => plugin.Id == id && plugin.Pending != PluginPendingChange.Remove)?.Version is { Length: > 0 } version
			? version
			: null;

	// The folder the next start loads the plugin from: a waiting install wins over what is installed now.
	private string? NextStartFolder(string id)
	{
		if (!PluginManifestReader.IsValidId(id))
		{
			return null;
		}

		string pending = Path.Combine(PendingDirectory, id);
		if (System.IO.Directory.Exists(pending))
		{
			return pending;
		}

		string installed = Path.Combine(Directory, id);
		return System.IO.Directory.Exists(installed) ? installed : null;
	}

	private static PluginManifest? ReadManifest(string folder)
	{
		try
		{
			string path = Path.Combine(folder, PluginLayout.ManifestFile);
			return File.Exists(path) ? PluginManifestReader.Read(File.ReadAllBytes(path)) : null;
		}
		catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private void EnsureUserManaged()
	{
		if (!CanInstall)
		{
			throw new InvalidOperationException("Plugins here come from the operator's folder and cannot be changed from the app.");
		}
	}

	private static void TryDeleteFolder(string path)
	{
		try
		{
			if (System.IO.Directory.Exists(path))
			{
				System.IO.Directory.Delete(path, recursive: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// The next start deletes what is left of it.
		}
	}
}
