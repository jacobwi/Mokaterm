using System.Reflection;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Core.Plugins;

/// <summary>Loads a code plugin's assembly into its own context and creates the module it names.</summary>
internal static class PluginAssemblyLoader
{
	/// <exception cref="InvalidDataException">The assembly is not a usable plugin; the message is for the user.</exception>
	public static LoadedPlugin Load(string folder, PluginManifest manifest, ISet<string> takenAssemblyNames)
	{
		string assemblyFile = manifest.Assembly ?? throw new InvalidOperationException("The plugin carries no code.");
		string assemblyPath = Path.Combine(folder, PluginLayout.LibFolder, assemblyFile);
		if (!File.Exists(assemblyPath))
		{
			throw new InvalidDataException($"The plugin names {assemblyFile}, which is not in its lib folder.");
		}

		// Its files are served at _content/<assembly name>/, where a built-in module or another plugin may already be.
		string assemblyName;
		try
		{
			assemblyName = AssemblyName.GetAssemblyName(assemblyPath).Name ?? Path.GetFileNameWithoutExtension(assemblyFile);
		}
		catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or IOException)
		{
			throw new InvalidDataException($"{assemblyFile} is not a .NET assembly.", ex);
		}
		if (PluginLoadContext.IsHostAssembly(assemblyName) || !takenAssemblyNames.Add(assemblyName))
		{
			throw new InvalidDataException($"The plugin's assembly is named {assemblyName}, which Mokaterm or another plugin already uses.");
		}

		Assembly assembly;
		IMokatermModule module;
		try
		{
			assembly = new PluginLoadContext(assemblyPath).LoadFromAssemblyPath(assemblyPath);
			module = CreateModule(assembly);
		}
		catch (Exception ex) when (ex is FileLoadException or FileNotFoundException or BadImageFormatException or TypeLoadException
			or MissingMethodException or TargetInvocationException or ReflectionTypeLoadException or MemberAccessException)
		{
			throw new InvalidDataException($"The plugin could not be loaded: {ex.GetType().Name}. It may need a different Mokaterm version.", ex);
		}

		string webRoot = Path.Combine(folder, PluginLayout.WebRootFolder);
		bool hasWebRoot = Directory.Exists(webRoot);
		string styleBundle = $"{assemblyName}.bundle.scp.css";
		PluginInfo info = new(manifest.Id, manifest.Name, manifest.Version, assemblyName)
		{
			WebRoot = hasWebRoot ? webRoot : null,
			StyleSheet = hasWebRoot && File.Exists(Path.Combine(webRoot, styleBundle)) ? $"_content/{assemblyName}/{styleBundle}" : null,
		};
		return new LoadedPlugin(info, module);
	}

	private static IMokatermModule CreateModule(Assembly assembly)
	{
		Type moduleType = assembly.GetCustomAttribute<MokatermPluginAttribute>()?.ModuleType
			?? throw new InvalidDataException("The plugin's assembly does not name its module with [assembly: MokatermPlugin(...)].");
		if (!typeof(IMokatermModule).IsAssignableFrom(moduleType) || moduleType.GetConstructor(Type.EmptyTypes) is null)
		{
			throw new InvalidDataException($"{moduleType.Name} is not a Mokaterm module with a public constructor that takes nothing.");
		}

		return (IMokatermModule)Activator.CreateInstance(moduleType)!;
	}
}
