using System.Text;
using Mokaterm.Abstractions.Import;
using Mokaterm.Core.Import;

namespace Mokaterm.Core.Tests.Import;

public sealed class WinScpImportTests
{
	private const string Ini = """
		[Configuration\Interface]
		Theme=Dark

		[Sessions\Default%20Settings]
		FSProtocol=1

		[Sessions\prod%2Fweb]
		HostName=10.0.0.1
		UserName=deploy
		PortNumber=2121
		FSProtocol=5
		Ftps=3
		Password=A35C43F1A9B2

		[Sessions\implicit]
		HostName=ftps.example.com
		FSProtocol=5
		Ftps=1

		[Sessions\files]
		HostName=sftp.example.com
		UserName=bob
		FSProtocol=1

		[Sessions\legacy]
		HostName=scp.example.com
		FSProtocol=0

		[Sessions\share]
		HostName=dav.example.com
		FSProtocol=6

		[Sessions\bucket]
		HostName=s3.example.com
		FSProtocol=7

		""";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ReadAsync_MapsFtpSitesWithTheirEncryptionMode()
	{
		ImportPreview preview = await ReadAsync(Ini);

		ImportedEntry web = Assert.Single(preview.Entries, entry => entry.Name == "web");
		Assert.Equal("ftp", web.ProtocolId);
		Assert.Equal("10.0.0.1", web.Address);
		Assert.Equal("deploy", web.Username);
		Assert.Equal(2121, web.Port);
		Assert.Equal("prod", web.FolderPath);
		Assert.Equal("Explicit", web.Options.GetString("ftp.encryption"));

		ImportedEntry implicitTls = Assert.Single(preview.Entries, entry => entry.Name == "implicit");
		Assert.Equal("Implicit", implicitTls.Options.GetString("ftp.encryption"));
	}

	[Fact]
	public async Task ReadAsync_MapsSftpAndScpSitesToSftp()
	{
		ImportPreview preview = await ReadAsync(Ini);

		ImportedEntry files = Assert.Single(preview.Entries, entry => entry.Name == "files");
		Assert.Equal("sftp", files.ProtocolId);
		Assert.Null(files.Port);
		Assert.Empty(files.Options);

		ImportedEntry legacy = Assert.Single(preview.Entries, entry => entry.Name == "legacy");
		Assert.Equal("sftp", legacy.ProtocolId);
		Assert.Contains("SCP", legacy.Notes, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ReadAsync_NeverCarriesAStoredPasswordAcross()
	{
		ImportPreview preview = await ReadAsync(Ini);

		foreach (ImportedEntry entry in preview.Entries)
		{
			Assert.DoesNotContain(entry.Options, pair => pair.Key.Contains("password", StringComparison.OrdinalIgnoreCase));
			Assert.DoesNotContain("A35C43F1A9B2", $"{entry.Notes}{entry.Username}{entry.IdentityFilePath}", StringComparison.Ordinal);
		}
	}

	[Fact]
	public async Task ReadAsync_SkipsProtocolsMokatermDoesNotHave()
	{
		ImportPreview preview = await ReadAsync(Ini);

		Assert.Contains(preview.Skipped, skip => skip.Name == "share" && skip.Reason.Contains("WebDAV", StringComparison.Ordinal));
		Assert.Contains(preview.Skipped, skip => skip.Name == "bucket" && skip.Reason.Contains("S3", StringComparison.Ordinal));
		Assert.DoesNotContain(preview.Entries, entry => entry.Name == "Default Settings");
	}

	[Fact]
	public async Task ReadAsync_SaysSoWhenAFileHasNoSites()
	{
		ImportPreview preview = await ReadAsync("[Configuration\\Interface]\nTheme=Dark\n");

		Assert.Equal(ImportAvailability.NotFound, preview.Availability);
		Assert.NotNull(preview.Message);
	}

	[Fact]
	public async Task ReadAsync_ReadsTheRegistryWhenNoFileIsGiven()
	{
		WinScpImportSource source = new(() =>
		[
			new ImportSection(
				WinScpImportSource.RegistryPath + @"\box",
				new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					["HostName"] = "10.0.0.8",
					["FSProtocol"] = "1",
				}),
		]);

		ImportPreview preview = await source.ReadAsync(new ImportReadRequest(), Ct);

		Assert.Equal("sftp", Assert.Single(preview.Entries).ProtocolId);
	}

	private static async Task<ImportPreview> ReadAsync(string ini)
	{
		WinScpImportSource source = new(static () => []);
		return await source.ReadAsync(
			new ImportReadRequest(new ImportFile { Name = "WinSCP.ini", Content = Encoding.UTF8.GetBytes(ini) }),
			Ct);
	}
}
