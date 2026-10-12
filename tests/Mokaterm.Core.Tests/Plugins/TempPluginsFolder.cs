using Mokaterm.Abstractions.Platform;
using Mokaterm.Core.Plugins;

namespace Mokaterm.Core.Tests.Plugins;

/// <summary>A plugins folder of its own for one test, removed afterwards.</summary>
internal sealed class TempPluginsFolder : IDisposable
{
	public TempPluginsFolder()
	{
		Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mokaterm-core-tests", "plugins-" + Guid.NewGuid().ToString("N"));
	}

	public string Path { get; }

	public PluginCatalog Open(bool userManaged = true, bool skipAll = false, HostKind host = HostKind.Desktop) =>
		PluginCatalog.Open(new PluginCatalogOptions { Directory = Path, Host = host, UserManaged = userManaged, SkipAll = skipAll });

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(Path))
			{
				Directory.Delete(Path, recursive: true);
			}
		}
		catch (IOException)
		{
			// A leftover temp folder fails nothing.
		}
	}
}
