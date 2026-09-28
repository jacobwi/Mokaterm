using System.Text;
using Mokaterm.Abstractions.Import;
using Mokaterm.Core.Import;

namespace Mokaterm.Core.Tests.Import;

public sealed class PuttyImportTests
{
	private const string Export = """
		Windows Registry Editor Version 5.00

		[HKEY_CURRENT_USER\Software\SimonTatham\PuTTY\Sessions\Default%20Settings]
		"Protocol"="ssh"
		"PortNumber"=dword:00000016

		[HKEY_CURRENT_USER\Software\SimonTatham\PuTTY\Sessions\web%20prod]
		"HostName"="10.0.0.1"
		"PortNumber"=dword:0000087a
		"UserName"="deploy"
		"Protocol"="ssh"
		"PublicKeyFile"="C:\\keys\\deploy.ppk"

		[HKEY_CURRENT_USER\Software\SimonTatham\PuTTY\Sessions\hexuser]
		"HostName"="10.0.0.4"
		"Protocol"="ssh"
		"UserName"=hex(1):64,00,65,00,76,00,00,00

		[HKEY_CURRENT_USER\Software\SimonTatham\PuTTY\Sessions\mail]
		"HostName"="10.0.0.3"
		"Protocol"="telnet"
		"PortNumber"=dword:00000017

		[HKEY_CURRENT_USER\Software\SimonTatham\PuTTY\Sessions\console]
		"HostName"="COM3"
		"Protocol"="serial"

		[HKEY_CURRENT_USER\Software\SimonTatham\PuTTY\Sessions\nohost]
		"Protocol"="ssh"

		[HKEY_CURRENT_USER\Software\SimonTatham\PuTTY\Jumplist]
		"Recent sessions"=hex(7):77,00,65,00,62,00,00,00,00,00

		""";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ReadAsync_MapsSessionsFromAUtf16RegExport()
	{
		ImportPreview preview = await ReadAsync(Export, Encoding.Unicode);

		Assert.Equal(ImportAvailability.Found, preview.Availability);
		ImportedEntry web = Assert.Single(preview.Entries, entry => entry.Name == "web prod");
		Assert.Equal("10.0.0.1", web.Address);
		Assert.Equal(2170, web.Port);
		Assert.Equal("deploy", web.Username);
		Assert.Equal("ssh", web.ProtocolId);
		Assert.Equal(@"C:\keys\deploy.ppk", web.IdentityFilePath);

		// Telnet maps to our reserved id; whether it can be created is the importer's call, not the reader's.
		ImportedEntry mail = Assert.Single(preview.Entries, entry => entry.Name == "mail");
		Assert.Equal("telnet", mail.ProtocolId);
		Assert.Null(mail.Port);
	}

	[Fact]
	public async Task ReadAsync_DecodesHexEncodedStringValues()
	{
		ImportPreview preview = await ReadAsync(Export, Encoding.Unicode);

		Assert.Equal("dev", Assert.Single(preview.Entries, entry => entry.Name == "hexuser").Username);
	}

	[Fact]
	public async Task ReadAsync_SkipsTheTemplateUnsupportedProtocolsAndSessionsWithNoHost()
	{
		ImportPreview preview = await ReadAsync(Export, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

		Assert.DoesNotContain(preview.Entries, entry => entry.Name == "Default Settings");
		Assert.Contains(preview.Skipped, skip => skip.Name == "console" && skip.Reason.Contains("serial", StringComparison.Ordinal));
		Assert.Contains(preview.Skipped, skip => skip.Name == "nohost" && skip.Reason.Contains("no host name", StringComparison.Ordinal));
	}

	[Fact]
	public async Task ReadAsync_SaysSoWhenAnExportHasNoSessions()
	{
		ImportPreview preview = await ReadAsync("Windows Registry Editor Version 5.00\n\n[HKEY_CURRENT_USER\\Software\\Other]\n\"A\"=\"b\"\n", Encoding.Unicode);

		Assert.Equal(ImportAvailability.NotFound, preview.Availability);
		Assert.NotNull(preview.Message);
	}

	[Fact]
	public void RegFileReader_JoinsHexValuesSplitOverSeveralLines()
	{
		IReadOnlyList<ImportSection> sections = RegFileReader.ParseText(
			"[HKCU\\A]\n\"Name\"=hex(1):64,00,65,00,\\\n  76,00,00,00\n");

		Assert.Equal("dev", Assert.Single(sections).Values["Name"]);
	}

	[Fact]
	public void RegFileReader_IgnoresKeysAnExportDeletes()
	{
		IReadOnlyList<ImportSection> sections = RegFileReader.ParseText("[-HKCU\\Gone]\n\"Name\"=\"x\"\n[HKCU\\Kept]\n\"Name\"=\"y\"\n");

		Assert.Equal("HKCU\\Kept", Assert.Single(sections).Path);
	}

	[Fact]
	public async Task ReadAsync_ReadsTheRegistryWhenNoFileIsGiven()
	{
		PuttyImportSource source = new(() =>
		[
			new ImportSection(
				PuttyImportSource.RegistryPath + @"\box",
				new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					["HostName"] = "10.0.0.7",
					["Protocol"] = "ssh",
					["PortNumber"] = "22",
				}),
		]);

		ImportPreview preview = await source.ReadAsync(new ImportReadRequest(), Ct);

		ImportedEntry entry = Assert.Single(preview.Entries);
		Assert.Equal("box", entry.Name);
		Assert.Null(entry.Port);
	}

	private static async Task<ImportPreview> ReadAsync(string text, Encoding encoding)
	{
		byte[] content = [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];
		PuttyImportSource source = new(static () => []);
		return await source.ReadAsync(new ImportReadRequest(new ImportFile { Name = "putty.reg", Content = content }), Ct);
	}
}
