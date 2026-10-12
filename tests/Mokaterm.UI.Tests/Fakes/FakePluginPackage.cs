using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>A package as the manager would hand one to the install dialog, already read and checked.</summary>
internal sealed class FakePluginPackage : IPluginPackage
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public string Version { get; init; } = "1.0.0";

	public string? Publisher { get; init; }

	public string? Description { get; init; }

	public bool HasCode { get; init; }

	public IReadOnlyList<string> Hosts { get; init; } = [];

	public int ThemeCount { get; init; }

	public IReadOnlyList<PluginPackageFile> Files { get; init; } = [];

	public string Hash { get; init; } = new('0', 64);

	public string? InstalledVersion { get; init; }

	public string? Problem { get; init; }

	public bool IsDisposed { get; private set; }

	public void Dispose() => IsDisposed = true;
}
