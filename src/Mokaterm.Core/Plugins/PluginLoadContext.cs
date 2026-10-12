using System.Reflection;
using System.Runtime.Loader;

namespace Mokaterm.Core.Plugins;

/// <summary>
/// One plugin's assemblies. Anything the host ships comes from the host, because a second copy of an assembly is a
/// second set of types: a plugin's own copy of <c>ComponentBase</c> or <c>IMokatermModule</c> would match nothing the
/// shell asks for. Everything else, native libraries included, comes from the plugin's folder.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
	private static readonly Lazy<HashSet<string>> HostAssemblies = new(ReadHostAssemblies);

	private readonly AssemblyDependencyResolver _resolver;

	// Not collectible: Blazor caches what it learns about component types, so a plugin stays until the app ends.
	public PluginLoadContext(string mainAssemblyPath)
		: base(Path.GetFileNameWithoutExtension(mainAssemblyPath), isCollectible: false)
	{
		_resolver = new AssemblyDependencyResolver(mainAssemblyPath);
	}

	/// <summary>True for an assembly the host ships, which a plugin always shares instead of bringing its own.</summary>
	public static bool IsHostAssembly(string name) => HostAssemblies.Value.Contains(name);

	protected override Assembly? Load(AssemblyName assemblyName)
	{
		if (assemblyName.Name is { } name && IsHostAssembly(name))
		{
			return null;
		}

		string? path = _resolver.ResolveAssemblyToPath(assemblyName);
		return path is null ? null : LoadFromAssemblyPath(path);
	}

	protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
	{
		string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
		return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
	}

	// The trusted platform list is every assembly the host was published with, loaded yet or not, so a shared assembly
	// the host has not touched by the time a plugin asks for it still comes from the host.
	private static HashSet<string> ReadHostAssemblies()
	{
		HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
		if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string list)
		{
			foreach (string path in list.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
			{
				names.Add(Path.GetFileNameWithoutExtension(path));
			}
		}

		foreach (Assembly loaded in Default.Assemblies)
		{
			if (loaded.GetName().Name is { } name)
			{
				names.Add(name);
			}
		}

		return names;
	}
}
