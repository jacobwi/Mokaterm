using System.IO.Compression;
using System.Reflection;

namespace Mokaterm.Tests.Shared;

/// <summary>
/// The .mokaplugin the plugin SDK builds from samples/Mokaterm.Plugins.Sample: a real code plugin, with a view, a script,
/// scoped styles and a theme. A test project gets it by importing Shared/Plugins/SamplePluginPackage.targets.
/// </summary>
internal static class SamplePluginPackage
{
	public const string Id = "moka.sample";

	public const string AssemblyName = "Mokaterm.Plugins.Sample";

	/// <summary>Where the package sits in the test output.</summary>
	public static string FilePath { get; } = Find();

	public static byte[] ReadAllBytes() => File.ReadAllBytes(FilePath);

	public static ZipArchive Open() => new(File.OpenRead(FilePath), ZipArchiveMode.Read);

	private static string Find()
	{
		string relative = typeof(SamplePluginPackage).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(attribute => attribute.Key == "SamplePluginPackage")?.Value
			?? throw new InvalidOperationException("This test project does not import Shared/Plugins/SamplePluginPackage.targets.");
		string path = Path.Combine(AppContext.BaseDirectory, relative);
		return File.Exists(path)
			? path
			: throw new FileNotFoundException("The sample plugin's package was not copied next to the tests; build the test project again.", path);
	}
}
