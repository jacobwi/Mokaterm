using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>
/// A plugin manager over a fixed list, for rendering the Plugins page. A static render presses no button, so the
/// changes only record what was asked and reading a package is never reached.
/// </summary>
internal sealed class FakePluginManager : IPluginManager
{
	private readonly List<string> _calls = [];

	public event EventHandler? Changed;

	public string Directory { get; init; } = Path.Combine("C:", "data", "plugins");

	public bool CanInstall { get; init; } = true;

	public bool IsSafeStart { get; init; }

	public bool RestartRequired { get; init; }

	public IReadOnlyList<PluginRecord> Plugins { get; init; } = [];

	/// <summary>Every change asked for, as <c>install id on</c>, <c>enable id off</c> or <c>remove id</c>.</summary>
	public IReadOnlyList<string> Calls => _calls;

	public Task<IPluginPackage> ReadPackageAsync(Stream package, PluginPackageOrigin? origin, CancellationToken cancellationToken) =>
		throw new NotSupportedException();

	public Task InstallAsync(IPluginPackage package, bool enable, CancellationToken cancellationToken)
	{
		_calls.Add($"install {package.Id} {(enable ? "on" : "off")}");
		Changed?.Invoke(this, EventArgs.Empty);
		return Task.CompletedTask;
	}

	public Task<PluginFileSet> ReadFilesAsync(string id, CancellationToken cancellationToken) =>
		Task.FromResult(new PluginFileSet([new PluginPackageFile("plugin.json", 120, new string('a', 64))], new string('b', 64)));

	public Task SetEnabledAsync(string id, bool enabled, string? trustedHash, CancellationToken cancellationToken)
	{
		_calls.Add($"enable {id} {(enabled ? "on" : "off")}");
		Changed?.Invoke(this, EventArgs.Empty);
		return Task.CompletedTask;
	}

	public Task RemoveAsync(string id, CancellationToken cancellationToken)
	{
		_calls.Add($"remove {id}");
		Changed?.Invoke(this, EventArgs.Empty);
		return Task.CompletedTask;
	}
}
