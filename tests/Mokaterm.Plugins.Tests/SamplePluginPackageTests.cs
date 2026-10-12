using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mokaterm.Abstractions.Modules;

namespace Mokaterm.Plugins.Tests;

/// <summary>
/// The package the plugin SDK builds from samples/Mokaterm.Plugins.Sample, read the way the app reads one. This process
/// references Core and UI.Common like a host does, so what it can load stands in for what a host provides.
/// </summary>
public sealed class SamplePluginPackageTests
{
	private const string AssemblyFile = SamplePluginPackage.AssemblyName + ".dll";

	private static readonly string[] TopLevelFolders = ["lib/", "wwwroot/", "themes/"];

	[Fact]
	public void Manifest_DescribesThePlugin()
	{
		using ZipArchive zip = SamplePluginPackage.Open();
		using JsonDocument manifest = JsonDocument.Parse(ReadAllBytes(zip, "plugin.json"));
		JsonElement root = manifest.RootElement;

		Assert.Equal(SamplePluginPackage.Id, root.GetProperty("id").GetString());
		Assert.Equal("Sample plugin", root.GetProperty("name").GetString());
		Assert.Equal("Moka", root.GetProperty("publisher").GetString());
		Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("description").GetString()));
		Assert.Equal(AssemblyFile, root.GetProperty("assembly").GetString());
		Assert.Equal(["desktop", "web"], root.GetProperty("hosts").EnumerateArray().Select(host => host.GetString()));
	}

	[Fact]
	public void Manifest_NamesTheContractsVersionThisHostRuns()
	{
		using ZipArchive zip = SamplePluginPackage.Open();
		using JsonDocument manifest = JsonDocument.Parse(ReadAllBytes(zip, "plugin.json"));

		// The app compares sdk with its own Mokaterm.Abstractions, which is the one this process loaded.
		Version contracts = typeof(IMokatermModule).Assembly.GetName().Version!;
		Assert.Equal($"{contracts.Major}.{contracts.Minor}", manifest.RootElement.GetProperty("sdk").GetString());
	}

	[Fact]
	public void FileName_IsTheIdAndTheVersion()
	{
		using ZipArchive zip = SamplePluginPackage.Open();
		using JsonDocument manifest = JsonDocument.Parse(ReadAllBytes(zip, "plugin.json"));
		string version = manifest.RootElement.GetProperty("version").GetString()!;

		Assert.False(string.IsNullOrWhiteSpace(version));
		Assert.Equal($"{SamplePluginPackage.Id}-{version}.mokaplugin", Path.GetFileName(SamplePluginPackage.FilePath));
	}

	[Fact]
	public void Lib_HoldsThePluginItsDepsFileAndItsSymbols()
	{
		using ZipArchive zip = SamplePluginPackage.Open();

		// The sample has no dependency of its own, so these are all it should carry.
		Assert.Equal(
			[$"lib/{SamplePluginPackage.AssemblyName}.deps.json", $"lib/{AssemblyFile}", $"lib/{SamplePluginPackage.AssemblyName}.pdb"],
			EntryNames(zip).Where(name => name.StartsWith("lib/", StringComparison.Ordinal)));
	}

	[Fact]
	public void Lib_CarriesNothingTheHostProvides()
	{
		using ZipArchive zip = SamplePluginPackage.Open();
		HashSet<string> host = HostAssemblyNames();

		IEnumerable<string> carried = EntryNames(zip)
			.Where(name => name.StartsWith("lib/", StringComparison.Ordinal) && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
			.Select(name => Path.GetFileNameWithoutExtension(name))
			.Where(name => name != SamplePluginPackage.AssemblyName);
		Assert.DoesNotContain(carried, host.Contains);
		Assert.DoesNotContain(EntryNames(zip), name => Regex.IsMatch(name, @"^lib/(Mokaterm\.(Abstractions|UI\.Common)|Moka\.Red|Microsoft\.|System\.)", RegexOptions.IgnoreCase));
	}

	[Fact]
	public void Lib_ResolvesEverythingThePluginReferences()
	{
		using ZipArchive zip = SamplePluginPackage.Open();
		HashSet<string> host = HostAssemblyNames();
		HashSet<string> carried = EntryNames(zip)
			.Where(name => name.StartsWith("lib/", StringComparison.Ordinal) && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
			.Select(name => Path.GetFileNameWithoutExtension(name))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		using PEReader assembly = new(new MemoryStream(ReadAllBytes(zip, $"lib/{AssemblyFile}")));
		MetadataReader metadata = assembly.GetMetadataReader();
		List<string> missing = [];
		foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
		{
			string name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
			if (!host.Contains(name) && !carried.Contains(name))
			{
				missing.Add(name);
			}
		}

		Assert.Empty(missing);
	}

	[Fact]
	public void WebRoot_HoldsTheScriptAndTheScopedStylesUnderTheirPlainNames()
	{
		using ZipArchive zip = SamplePluginPackage.Open();

		// No compressed copies, no application bundle and no fingerprints: the host serves the folder as it is.
		Assert.Equal(
			[$"wwwroot/{SamplePluginPackage.AssemblyName}.bundle.scp.css", "wwwroot/js/sample.js"],
			EntryNames(zip).Where(name => name.StartsWith("wwwroot/", StringComparison.Ordinal)));
		string styles = ReadText(zip, $"wwwroot/{SamplePluginPackage.AssemblyName}.bundle.scp.css");
		Assert.Matches(@"\.sample-plugin\[b-[a-z0-9]+\]", styles);
		Assert.Contains("export function stamp", ReadText(zip, "wwwroot/js/sample.js"), StringComparison.Ordinal);
	}

	[Fact]
	public void Themes_AreCarriedAsTheyWereWritten()
	{
		using ZipArchive zip = SamplePluginPackage.Open();

		Assert.Equal(["themes/moka-sample.json"], EntryNames(zip).Where(name => name.StartsWith("themes/", StringComparison.Ordinal)));
		using JsonDocument theme = JsonDocument.Parse(ReadAllBytes(zip, "themes/moka-sample.json"));
		Assert.Equal("Moka Sample", theme.RootElement.GetProperty("name").GetString());
	}

	[Fact]
	public void Entries_AreFilesInTheLayoutTheAppAccepts()
	{
		using ZipArchive zip = SamplePluginPackage.Open();
		List<string> names = EntryNames(zip);

		Assert.All(names, name =>
		{
			Assert.DoesNotContain('\\', name);
			Assert.False(name.EndsWith('/'), $"{name} is a folder entry");
			Assert.True(name == "plugin.json" || TopLevelFolders.Any(folder => name.StartsWith(folder, StringComparison.Ordinal)), $"{name} is outside the layout");
		});
	}

	[Fact]
	public void Entries_AreWrittenTheSameWayEveryBuild()
	{
		using ZipArchive zip = SamplePluginPackage.Open();
		List<string> names = EntryNames(zip);

		Assert.Equal(names.Order(StringComparer.Ordinal), names);
		Assert.Single(zip.Entries.Select(entry => entry.LastWriteTime).Distinct());
	}

	private static List<string> EntryNames(ZipArchive zip) => zip.Entries.Select(entry => entry.FullName).ToList();

	private static byte[] ReadAllBytes(ZipArchive zip, string name)
	{
		ZipArchiveEntry entry = zip.GetEntry(name) ?? throw new InvalidOperationException($"The package has no {name}.");
		using Stream stream = entry.Open();
		using MemoryStream copy = new();
		stream.CopyTo(copy);
		return copy.ToArray();
	}

	private static string ReadText(ZipArchive zip, string name) => System.Text.Encoding.UTF8.GetString(ReadAllBytes(zip, name));

	private static HashSet<string> HostAssemblyNames() =>
		((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
			.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
			.Select(path => Path.GetFileNameWithoutExtension(path))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
}
