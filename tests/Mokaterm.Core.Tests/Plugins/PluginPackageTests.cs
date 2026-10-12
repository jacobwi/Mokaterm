using Mokaterm.Abstractions.Plugins;
using Mokaterm.Core.Plugins;

namespace Mokaterm.Core.Tests.Plugins;

public sealed class PluginPackageTests
{
	[Fact]
	public void Read_ThemePack_ListsItsFilesWithHashes()
	{
		using PluginPackage package = Read(TestPlugins.ThemePack("moka.themes"));

		Assert.Equal("moka.themes", package.Id);
		Assert.False(package.HasCode);
		Assert.Equal(1, package.ThemeCount);
		Assert.Equal(["plugin.json", "themes/dracula.json"], package.Files.Select(file => file.Path));
		Assert.All(package.Files, file => Assert.Equal(64, file.Sha256.Length));
		Assert.Null(package.Problem);
		Assert.Null(package.InstalledVersion);
	}

	[Fact]
	public void Read_SameFiles_GiveTheSameHash_AndAnyChangeGivesAnother()
	{
		using PluginPackage first = Read(TestPlugins.CodePlugin("moka.code"));
		using PluginPackage again = Read(TestPlugins.CodePlugin("moka.code"));
		using PluginPackage changed = Read(TestPlugins.CodePlugin("moka.code", "1.0.1"));

		Assert.Equal(first.Hash, again.Hash);
		Assert.NotEqual(first.Hash, changed.Hash);
	}

	[Theory]
	[InlineData("../escape.txt")]
	[InlineData("lib/../../escape.dll")]
	[InlineData("/rooted.txt")]
	[InlineData("C:/drive.txt")]
	[InlineData("lib//double.dll")]
	[InlineData("other/file.txt")]
	[InlineData("README.md")]
	public void Read_RefusesPathsOutsideThePackageLayout(string path)
	{
		Dictionary<string, string> files = TestPlugins.ThemePack("moka.themes");
		files[path] = "x";

		Assert.Throws<InvalidDataException>(() => Read(files));
	}

	[Fact]
	public void Read_RefusesTheSamePathTwice()
	{
		Dictionary<string, string> files = TestPlugins.ThemePack("moka.themes");
		files["themes/DRACULA.json"] = "{}";

		InvalidDataException error = Assert.Throws<InvalidDataException>(() => Read(files));

		Assert.Contains("twice", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_WithoutManifest_IsNotAPlugin()
	{
		Assert.Throws<InvalidDataException>(() => Read(new Dictionary<string, string> { ["themes/a.json"] = "{}" }));
	}

	[Fact]
	public void Read_CodePluginMissingItsAssembly_IsRefused()
	{
		Dictionary<string, string> files = TestPlugins.CodePlugin("moka.code");
		files.Remove("lib/moka.code.dll");

		Assert.Throws<InvalidDataException>(() => Read(files));
	}

	[Fact]
	public void Read_DataPluginWithALibFolder_IsRefused()
	{
		Dictionary<string, string> files = TestPlugins.ThemePack("moka.themes");
		files["lib/hidden.dll"] = "x";

		Assert.Throws<InvalidDataException>(() => Read(files));
	}

	[Fact]
	public void Read_NotAZip_SaysSo()
	{
		InvalidDataException error = Assert.Throws<InvalidDataException>(
			() => PluginPackage.Read("plain text"u8.ToArray(), _ => null, PluginHosts.Desktop));

		Assert.Contains("zip", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_PluginForAnotherHost_CarriesTheProblem()
	{
		Dictionary<string, string> files = TestPlugins.ThemePack("moka.themes");
		files["plugin.json"] = TestPlugins.Manifest("moka.themes", hosts: "\"desktop\"");

		using PluginPackage package = PluginPackage.Read(TestPlugins.Zip(files), _ => null, PluginHosts.Web);

		Assert.NotNull(package.Problem);
	}

	[Fact]
	public void Read_PluginBuiltForAnotherVersion_CarriesTheProblem()
	{
		using PluginPackage package = Read(TestPlugins.CodePlugin("moka.code", sdk: "99.0"));

		Assert.Contains("built for Mokaterm 99.x", package.Problem, StringComparison.Ordinal);
	}

	[Fact]
	public void ExtractTo_WritesTheFilesWhoseHashesWereShown()
	{
		using TempPluginsFolder temp = new();
		using PluginPackage package = Read(TestPlugins.CodePlugin("moka.code"));
		string folder = Path.Combine(temp.Path, "moka.code");

		package.ExtractTo(folder);

		Assert.Equal(package.Hash, PluginHash.HashFolder(folder));
	}

	private static PluginPackage Read(IReadOnlyDictionary<string, string> files) =>
		PluginPackage.Read(TestPlugins.Zip(files), _ => null, PluginHosts.Desktop);
}
