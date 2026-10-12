using System.Text;
using Mokaterm.Core.Plugins;

namespace Mokaterm.Core.Tests.Plugins;

public sealed class PluginManifestReaderTests
{
	[Fact]
	public void Read_TakesEveryField()
	{
		PluginManifest manifest = Read(TestPlugins.Manifest("moka.sample", "1.2.3", "Moka.Sample.dll", hosts: "\"desktop\", \"WEB\""));

		Assert.Equal("moka.sample", manifest.Id);
		Assert.Equal("Plugin moka.sample", manifest.Name);
		Assert.Equal("1.2.3", manifest.Version);
		Assert.Equal("Tests", manifest.Publisher);
		Assert.Equal("Moka.Sample.dll", manifest.Assembly);
		Assert.Equal(TestPlugins.Sdk, manifest.Sdk);
		Assert.Equal(["desktop", "web"], manifest.Hosts);
		Assert.True(manifest.HasCode);
	}

	[Fact]
	public void Read_DataPlugin_NeedsNoSdk()
	{
		PluginManifest manifest = Read(TestPlugins.Manifest("moka.themes"));

		Assert.False(manifest.HasCode);
		Assert.Null(manifest.Sdk);
		Assert.Empty(manifest.Hosts);
	}

	[Theory]
	[InlineData("Moka.Sample")]
	[InlineData("moka..sample")]
	[InlineData("moka.sample.")]
	[InlineData("-moka")]
	[InlineData("moka/sample")]
	[InlineData("moka sample")]
	[InlineData("..")]
	public void Read_RefusesAnIdThatCannotBeAFolderName(string id)
	{
		InvalidDataException error = Assert.Throws<InvalidDataException>(() => Read(TestPlugins.Manifest(id)));

		Assert.Contains("id", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("../evil.dll")]
	[InlineData("lib/sample.dll")]
	[InlineData("sample.exe")]
	public void Read_RefusesAnAssemblyThatIsNotAPlainDllName(string assembly)
	{
		Assert.Throws<InvalidDataException>(() => Read(TestPlugins.Manifest("moka.sample", assembly: assembly)));
	}

	[Fact]
	public void Read_CodeWithoutSdk_IsRefused()
	{
		string json = """{ "id": "moka.sample", "name": "Sample", "version": "1.0.0", "assembly": "Sample.dll" }""";

		InvalidDataException error = Assert.Throws<InvalidDataException>(() => Read(json));

		Assert.Contains("sdk", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_UnknownHost_IsRefused()
	{
		Assert.Throws<InvalidDataException>(() => Read(TestPlugins.Manifest("moka.sample", hosts: "\"phone\"")));
	}

	[Theory]
	[InlineData("""{ "name": "Sample", "version": "1.0.0" }""")]
	[InlineData("""{ "id": "moka.sample", "version": "1.0.0" }""")]
	[InlineData("""{ "id": "moka.sample", "name": "Sample" }""")]
	[InlineData("not json")]
	public void Read_MissingFieldsOrBadJson_AreRefused(string json)
	{
		Assert.Throws<InvalidDataException>(() => Read(json));
	}

	private static PluginManifest Read(string json) => PluginManifestReader.Read(Encoding.UTF8.GetBytes(json));
}
