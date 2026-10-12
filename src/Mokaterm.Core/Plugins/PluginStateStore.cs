using System.Text.Json;
using Mokaterm.Core.Serialization;
using Mokaterm.Core.Storage;

namespace Mokaterm.Core.Plugins;

/// <summary>
/// <c>plugins.json</c>: which plugins are on, the files the user trusted for each code plugin, and the removals that
/// wait for the next start. A file that cannot be read counts as empty, which turns every code plugin off: losing the
/// trust is the safe way to fail.
/// </summary>
internal sealed class PluginStateStore
{
	private readonly string _path;

	public PluginStateStore(string pluginsDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);
		_path = Path.Combine(pluginsDirectory, PluginLayout.StateFile);
	}

	public PluginState Load()
	{
		try
		{
			if (!File.Exists(_path))
			{
				return new PluginState();
			}

			using FileStream stream = File.OpenRead(_path);
			return Normalize(JsonSerializer.Deserialize<PluginState>(stream, MokatermJson.Document));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			return new PluginState();
		}
	}

	public Task SaveAsync(PluginState state, CancellationToken cancellationToken) =>
		AtomicFile.WriteAsync(_path, JsonSerializer.SerializeToUtf8Bytes(state, MokatermJson.Document), cancellationToken);

	private static PluginState Normalize(PluginState? state) => state is null
		? new PluginState()
		: new PluginState
		{
			Plugins = state.Plugins is null ? new(StringComparer.Ordinal) : new(state.Plugins, StringComparer.Ordinal),
			Remove = [.. (state.Remove ?? []).Distinct(StringComparer.Ordinal)],
		};
}
