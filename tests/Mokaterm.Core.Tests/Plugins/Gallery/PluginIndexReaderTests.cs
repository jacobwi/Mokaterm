using System.Text;
using System.Text.Json.Nodes;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.Core.Plugins.Gallery;

namespace Mokaterm.Core.Tests.Plugins.Gallery;

/// <summary>
/// Reading <c>index.json</c> once its signature passed: every field held to the format's limits, a newer format answered
/// with "update Mokaterm", and only the versions this build can install offered.
/// </summary>
public sealed class PluginIndexReaderTests
{
	private static string Desktop => PluginHosts.Desktop;

	[Fact]
	public void Read_AFullEntry_KeepsEveryField()
	{
		JsonObject plugin = TestGallery.Plugin("moka.certificates", hasCode: true, TestGallery.Version("1.1.0"), TestGallery.Version("1.0.0"));
		plugin["verified"] = true;
		plugin["description"] = "Read, make and convert certificates.\nAnd check a server's.";

		PluginGalleryEntry entry = Assert.Single(Read(TestGallery.Index(plugin)).Plugins);

		Assert.Equal("moka.certificates", entry.Id);
		Assert.Equal("Plugin moka.certificates", entry.Name);
		Assert.Equal("Tests", entry.Publisher);
		Assert.Equal("Read, make and convert certificates.\nAnd check a server's.", entry.Description);
		Assert.Equal(["utilities", "server-tools"], entry.Categories);
		Assert.Equal(new Uri("https://github.com/acme/mokaterm-tests"), entry.Source);
		Assert.Equal("MIT", entry.License);
		Assert.True(entry.Verified);
		Assert.True(entry.HasCode);
		Assert.Equal([PluginHosts.Desktop, PluginHosts.Web], entry.Hosts);
		Assert.Equal(["1.1.0", "1.0.0"], entry.Versions.Select(version => version.Version));
		Assert.Equal(["1.1.0", "1.0.0"], entry.Offered.Select(version => version.Version));
		Assert.Null(entry.Unavailable);

		PluginGalleryVersion newest = entry.Offered[0];
		Assert.Equal(TestGallery.Sdk, newest.Sdk);
		Assert.Equal(TestGallery.PackageAddress("acme.listed", "1.1.0"), newest.Url);
		Assert.Equal(64, newest.Sha256.Length);
		Assert.Equal(5, newest.Size);
		Assert.Equal(TestGallery.Commit, newest.Commit);
		Assert.Equal(DateTimeOffset.Parse(TestGallery.Generated, System.Globalization.CultureInfo.InvariantCulture), newest.Published);
		Assert.Null(newest.Withdrawn);
	}

	[Fact]
	public void Read_ANewerFormat_AsksForAnUpdateOfMokaterm()
	{
		JsonObject index = TestGallery.Index();
		index["format"] = 2;

		// Whatever else a newer format changed, the answer is the same.
		index["plugins"] = "something new";

		PluginGalleryException error = Assert.Throws<PluginGalleryException>(() => Read(index));

		Assert.Contains("newer format", error.Message, StringComparison.Ordinal);
		Assert.Contains("Update Mokaterm", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("format", null)]
	[InlineData("format", "1")]
	[InlineData("format", 0)]
	[InlineData("generated", "yesterday")]
	[InlineData("plugins", null)]
	public void Read_WithABadTopLevelField_IsRefused(string field, object? value)
	{
		JsonObject index = TestGallery.Index();
		index[field] = JsonValue(value);

		Assert.Contains("The plugin list is damaged", Assert.Throws<PluginGalleryException>(() => Read(index)).Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("id", "Acme.Proxmox")]
	[InlineData("id", "acme..proxmox")]
	[InlineData("id", null)]
	[InlineData("name", "")]
	[InlineData("name", null)]
	[InlineData("name", "Tab\tin a name")]
	[InlineData("publisher", null)]
	[InlineData("description", null)]
	[InlineData("source", "http://github.com/acme/proxmox")]
	[InlineData("source", "github.com/acme/proxmox")]
	[InlineData("license", null)]
	[InlineData("verified", "yes")]
	[InlineData("hasCode", null)]
	[InlineData("categories", null)]
	[InlineData("hosts", null)]
	[InlineData("versions", null)]
	public void Read_WithABadPluginField_IsRefused(string field, object? value)
	{
		JsonObject plugin = TestGallery.Plugin("acme.proxmox", hasCode: true, TestGallery.Version("1.0.0"));
		plugin[field] = JsonValue(value);

		Assert.Contains("The plugin list is damaged", Assert.Throws<PluginGalleryException>(() => Read(TestGallery.Index(plugin))).Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("version", "")]
	[InlineData("sdk", "two")]
	[InlineData("sdk", "0.2.1")]
	[InlineData("url", "http://downloads.test/acme.proxmox-1.0.0.mokaplugin")]
	[InlineData("sha256", "ABCDEF")]
	[InlineData("sha256", "3F79BB7B435B05321651DAEFD374CDC681DC06FAA65E374E38337B88CA046DEA")]
	[InlineData("commit", "9f2c4e1")]
	[InlineData("size", 0)]
	[InlineData("size", (64 * 1024 * 1024) + 1)]
	[InlineData("size", "12")]
	[InlineData("published", null)]
	[InlineData("withdrawn", 5)]
	public void Read_WithABadVersionField_IsRefused(string field, object? value)
	{
		JsonObject version = TestGallery.Version("1.0.0");
		version[field] = JsonValue(value);

		Assert.Contains(
			"The plugin list is damaged",
			Assert.Throws<PluginGalleryException>(() => Read(TestGallery.Index(TestGallery.Plugin("acme.proxmox", hasCode: true, version)))).Message,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Read_TheSamePluginTwice_IsRefused()
	{
		JsonObject index = TestGallery.Index(
			TestGallery.Plugin("acme.proxmox", hasCode: true, TestGallery.Version("1.0.0")),
			TestGallery.Plugin("acme.proxmox", hasCode: true, TestGallery.Version("1.1.0")));

		Assert.Contains("lists acme.proxmox twice", Assert.Throws<PluginGalleryException>(() => Read(index)).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_TheSameVersionTwice_IsRefused()
	{
		JsonObject index = TestGallery.Index(TestGallery.Plugin("acme.proxmox", hasCode: true, TestGallery.Version("1.0.0"), TestGallery.Version("1.0.0")));

		Assert.Contains("version 1.0.0 twice", Assert.Throws<PluginGalleryException>(() => Read(index)).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_AFieldTwiceInOneObject_IsRefused()
	{
		byte[] json = Encoding.UTF8.GetBytes($"{{\"format\": 1, \"format\": 1, \"generated\": \"{TestGallery.Generated}\", \"plugins\": []}}");

		Assert.Throws<PluginGalleryException>(() => PluginIndexReader.Read(json, Desktop));
	}

	[Fact]
	public void Read_TextOver1000Characters_IsRefused()
	{
		JsonObject plugin = TestGallery.Plugin("acme.proxmox", hasCode: true, TestGallery.Version("1.0.0"));
		plugin["description"] = new string('a', 1001);

		Assert.Contains("longer than 1000 characters", Assert.Throws<PluginGalleryException>(() => Read(TestGallery.Index(plugin))).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_MoreThan100Versions_IsRefused()
	{
		JsonObject plugin = TestGallery.Plugin(
			"acme.proxmox",
			hasCode: true,
			[.. Enumerable.Range(0, 101).Select(minor => TestGallery.Version($"1.{minor}.0"))]);

		Assert.Contains("more than 100 versions", Assert.Throws<PluginGalleryException>(() => Read(TestGallery.Index(plugin))).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_MoreThan2000Plugins_IsRefused()
	{
		JsonObject index = TestGallery.Index(
			[.. Enumerable.Range(0, 2001).Select(number => TestGallery.Plugin($"acme.plugin{number:D4}", hasCode: false))]);

		Assert.Contains("more than 2000 plugins", Assert.Throws<PluginGalleryException>(() => Read(index)).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_SomethingThatIsNotJson_IsRefused()
	{
		PluginGalleryException error = Assert.Throws<PluginGalleryException>(() => PluginIndexReader.Read(Encoding.UTF8.GetBytes("<html>404</html>"), Desktop));

		Assert.Equal("The plugin list is not valid JSON, so it was not used.", error.Message);
	}

	[Fact]
	public void Read_OffersOnlyVersionsThisBuildLoads_NewestFirst()
	{
		JsonObject plugin = TestGallery.Plugin(
			"acme.proxmox",
			hasCode: true,
			TestGallery.Version("3.0.0", sdk: "9.9"),
			TestGallery.Version("2.0.0", withdrawn: "It deleted backups."),
			TestGallery.Version("1.1.0"),
			TestGallery.Version("1.0.0"));

		PluginGalleryEntry entry = Assert.Single(Read(TestGallery.Index(plugin)).Plugins);

		Assert.Equal(["3.0.0", "2.0.0", "1.1.0", "1.0.0"], entry.Versions.Select(version => version.Version));
		Assert.Equal(["1.1.0", "1.0.0"], entry.Offered.Select(version => version.Version));
		Assert.Equal("1.1.0", entry.Newest?.Version);
		Assert.Equal("It deleted backups.", entry.Versions[1].Withdrawn);
	}

	[Fact]
	public void Read_AThemePackBuiltForAnotherVersion_IsStillOffered()
	{
		// Data runs nothing, so the loader takes it on any version, and the gallery offers what the loader takes.
		JsonObject plugin = TestGallery.Plugin("acme.themes", hasCode: false, TestGallery.Version("1.0.0", sdk: "9.9"));

		Assert.Equal("1.0.0", Assert.Single(Read(TestGallery.Index(plugin)).Plugins).Newest?.Version);
	}

	[Fact]
	public void Read_APluginForAnotherHost_OffersNothingAndSaysWhy()
	{
		JsonObject plugin = TestGallery.Plugin("acme.serial", hasCode: true, TestGallery.Version("1.0.0"));
		plugin["hosts"] = new JsonArray("desktop");

		byte[] json = Encoding.UTF8.GetBytes(TestGallery.Index(plugin).ToJsonString());

		PluginGalleryEntry onDesktop = Assert.Single(PluginIndexReader.Read(json, Desktop).Plugins);
		PluginGalleryEntry onWeb = Assert.Single(PluginIndexReader.Read(json, PluginHosts.Web).Plugins);

		Assert.NotNull(onDesktop.Newest);
		Assert.Empty(onWeb.Offered);
		Assert.Equal("This plugin is for the desktop app only.", onWeb.Unavailable);
	}

	[Fact]
	public void Read_APluginWithNoHosts_IsForEveryHost()
	{
		JsonObject plugin = TestGallery.Plugin("acme.themes", hasCode: false, TestGallery.Version("1.0.0"));
		plugin["hosts"] = new JsonArray();

		Assert.NotNull(Assert.Single(Read(TestGallery.Index(plugin), PluginHosts.Web).Plugins).Newest);
	}

	[Fact]
	public void Read_EveryVersionWithdrawn_SaysSo()
	{
		JsonObject plugin = TestGallery.Plugin("acme.proxmox", hasCode: true, TestGallery.Version("1.0.0", withdrawn: ""));

		PluginGalleryEntry entry = Assert.Single(Read(TestGallery.Index(plugin)).Plugins);

		Assert.Empty(entry.Offered);
		Assert.Equal("Every version of it was withdrawn.", entry.Unavailable);
		Assert.Equal("The gallery gives no reason.", entry.Versions[0].Withdrawn);
	}

	[Fact]
	public void Read_NoVersionForThisMokaterm_SaysWhichOneItNeeds()
	{
		JsonObject plugin = TestGallery.Plugin("acme.proxmox", hasCode: true, TestGallery.Version("1.0.0", sdk: "9.9"));

		PluginGalleryEntry entry = Assert.Single(Read(TestGallery.Index(plugin)).Plugins);

		Assert.Equal($"No version of it is built for Mokaterm {TestGallery.Sdk} yet.", entry.Unavailable);
	}

	[Theory]
	[InlineData("1.0.0", "1.1.0", "1.1.0")]
	[InlineData("1.1.0", "1.1.0", null)]
	[InlineData("2.0.0", "1.1.0", null)]
	[InlineData("1.1.0-beta", "1.1.0", "1.1.0")]
	[InlineData("custom build", "1.1.0", null)]
	public void UpdateFor_OffersOnlyANewerVersion(string installed, string newest, string? expected)
	{
		PluginGalleryEntry entry = Assert.Single(Read(TestGallery.Index(TestGallery.Plugin("acme.proxmox", hasCode: true, TestGallery.Version(newest)))).Plugins);

		Assert.Equal(expected, entry.UpdateFor(installed)?.Version);
	}

	[Fact]
	public void WithdrawnFor_FindsTheInstalledVersionOnlyWhenItWasWithdrawn()
	{
		JsonObject plugin = TestGallery.Plugin(
			"acme.proxmox",
			hasCode: true,
			TestGallery.Version("1.1.0"),
			TestGallery.Version("1.0.0", withdrawn: "It deleted backups."));

		PluginGalleryEntry entry = Assert.Single(Read(TestGallery.Index(plugin)).Plugins);

		Assert.Equal("It deleted backups.", entry.WithdrawnFor("1.0.0")?.Withdrawn);
		Assert.Null(entry.WithdrawnFor("1.1.0"));
		Assert.Null(entry.WithdrawnFor("0.9.0"));
	}

	private static PluginGalleryIndex Read(JsonObject index, string? host = null) =>
		PluginIndexReader.Read(Encoding.UTF8.GetBytes(index.ToJsonString()), host ?? Desktop);

	private static JsonValue? JsonValue(object? value) => value switch
	{
		null => null,
		string text => System.Text.Json.Nodes.JsonValue.Create(text),
		int number => System.Text.Json.Nodes.JsonValue.Create(number),
		_ => throw new ArgumentException("Only text and numbers stand in for a field here.", nameof(value)),
	};
}
