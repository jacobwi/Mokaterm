using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Import;
using Mokaterm.Core.Import;

namespace Mokaterm.Core.Tests.Import;

public sealed class OpenSshConfigImportSourceTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ReadAsync_TurnsConcreteAliasesIntoEntriesAndLeavesWildcardsAsDefaults()
	{
		ImportPreview preview = await ReadAsync(
			"""
			Host web1 web2
				HostName %h.example.com
				IdentityFile ~/.ssh/id_ed25519

			Host db1
				HostName 10.0.0.9
				User postgres
				ProxyJump bastion.example.com

			Host *
				User deploy
				Port 2222
			""");

		Assert.Equal(ImportAvailability.Found, preview.Availability);
		Assert.Equal(["web1", "web2", "db1"], preview.Entries.Select(entry => entry.Name));

		ImportedEntry web1 = preview.Entries[0];
		Assert.Equal("web1.example.com", web1.Address);
		Assert.Equal("deploy", web1.Username);
		Assert.Equal(2222, web1.Port);
		Assert.Equal("~/.ssh/id_ed25519", web1.IdentityFilePath);
		Assert.Equal("ssh", web1.ProtocolId);

		ImportedEntry db1 = preview.Entries[2];
		Assert.Equal("10.0.0.9", db1.Address);

		// db1 sets User before the wildcard block does, and OpenSSH keeps the first value it reads.
		Assert.Equal("postgres", db1.Username);
		Assert.Contains("bastion.example.com", db1.Notes, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ReadAsync_LetsAWildcardBlockAtTheTopWinLikeOpenSshDoes()
	{
		ImportPreview preview = await ReadAsync(
			"""
			Host *
				User deploy

			Host db1
				HostName 10.0.0.9
				User postgres
			""");

		// This is why OpenSSH tells you to put "Host *" last: the first value read is the one that counts.
		Assert.Equal("deploy", Assert.Single(preview.Entries).Username);
	}

	[Fact]
	public async Task ReadAsync_FallsBackToTheAliasWhenThereIsNoHostName()
	{
		ImportPreview preview = await ReadAsync("Host jump.example.com\n  User ops");

		ImportedEntry entry = Assert.Single(preview.Entries);
		Assert.Equal("jump.example.com", entry.Address);
		Assert.Equal("ops", entry.Username);
	}

	[Fact]
	public async Task ReadAsync_DropsAPortThatOnlyRepeatsTheDefault()
	{
		ImportPreview preview = await ReadAsync("Host web1\n  Port 22");

		Assert.Null(Assert.Single(preview.Entries).Port);
	}

	[Fact]
	public async Task ReadAsync_SaysSoWhenAConfigHasOnlyWildcardBlocks()
	{
		ImportPreview preview = await ReadAsync("Host *\n  ServerAliveInterval 30");

		Assert.Equal(ImportAvailability.NotFound, preview.Availability);
		Assert.NotNull(preview.Message);
		Assert.Contains(preview.Skipped, skip => skip.Name == "Host *");
	}

	[Fact]
	public async Task ReadAsync_ReportsAMissingConfigInsteadOfFailing()
	{
		string home = TempFolder.NewPath("mokaterm-import-tests");
		OpenSshConfigImportSource source = new(() => home);

		ImportPreview preview = await source.ReadAsync(new ImportReadRequest(), Ct);

		Assert.Equal(ImportAvailability.NotFound, preview.Availability);
		Assert.Empty(preview.Entries);
	}

	[Fact]
	public async Task ReadAsync_ReadsTheConfigAndItsIncludesFromDisk()
	{
		await using TempFolder temp = new("mokaterm-import-tests");
		string home = temp.Path;
		string ssh = Path.Combine(home, ".ssh");
		string conf = Path.Combine(ssh, "conf.d");
		Directory.CreateDirectory(conf);
		await File.WriteAllTextAsync(Path.Combine(ssh, "config"), "Include conf.d/*.conf\nHost web1\n  HostName 10.0.0.1\n", Ct);
		await File.WriteAllTextAsync(Path.Combine(conf, "work.conf"), "Host db1\n  HostName 10.0.0.9\n  User postgres\n", Ct);
		OpenSshConfigImportSource source = new(() => home);

		ImportPreview preview = await source.ReadAsync(new ImportReadRequest(), Ct);

		Assert.Equal(ImportAvailability.Found, preview.Availability);
		Assert.Equal(["db1", "web1"], preview.Entries.Select(entry => entry.Name).Order(StringComparer.Ordinal));
		Assert.Equal(Path.Combine(ssh, "config"), preview.Location);
	}

	[Fact]
	public async Task ReadAsync_ThousandsOfPlainHosts_AreMatchedWithoutComparingEveryPair()
	{
		StringBuilder config = new();
		for (int i = 0; i < 20_000; i++)
		{
			config.Append(CultureInfo.InvariantCulture, $"Host h{i}\n  HostName 10.{i / 65536}.{i / 256 % 256}.{i % 256}\n");
		}

		config.Append("Host *\n  User deploy\n");

		ImportPreview preview = await ReadWithDeadlineAsync(config.ToString());

		Assert.Equal(20_000, preview.Entries.Count);
		Assert.All(preview.Entries, entry => Assert.Equal("deploy", entry.Username));
		Assert.Equal("10.0.78.31", preview.Entries.Single(entry => entry.Name == "h19999").Address);
		Assert.DoesNotContain(preview.Skipped, skip => skip.Reason.Contains("left out", StringComparison.Ordinal));
	}

	[Fact]
	public async Task ReadAsync_AConfigBuiltToMakeMatchingQuadratic_StopsEarlyAndSaysSo()
	{
		// Every block is a wildcard block that also names a host, so each host has to be matched against every block.
		StringBuilder config = new();
		for (int i = 0; i < 50_000; i++)
		{
			config.Append(CultureInfo.InvariantCulture, $"Host h{i} *.x{i}\n  User u{i}\n");
		}

		ImportPreview preview = await ReadWithDeadlineAsync(config.ToString());

		Assert.NotEmpty(preview.Entries);
		Assert.True(preview.Entries.Count < 50_000);
		Assert.Equal("u0", preview.Entries[0].Username);
		Assert.Contains(preview.Skipped, skip => skip.Reason.Contains("left out", StringComparison.Ordinal));
	}

	[Fact]
	public async Task ReadAsync_OneBlockWithEveryHostAndThousandsOfSettings_StaysLinear()
	{
		StringBuilder config = new("Host");
		for (int i = 0; i < 50_000; i++)
		{
			config.Append(CultureInfo.InvariantCulture, $" h{i}");
		}

		config.Append('\n');
		for (int i = 0; i < 50_000; i++)
		{
			config.Append(CultureInfo.InvariantCulture, $"  SetEnv V{i}=1\n");
		}

		config.Append("  User ops\n");

		ImportPreview preview = await ReadWithDeadlineAsync(config.ToString());

		Assert.Equal(50_000, preview.Entries.Count);
		Assert.All(preview.Entries, entry => Assert.Equal("ops", entry.Username));
	}

	/// <summary>Runs the synchronous read on the pool, so a regression fails the test instead of hanging the run.</summary>
	private static async Task<ImportPreview> ReadWithDeadlineAsync(string config) =>
		await Task.Run(() => ReadAsync(config), Ct).WaitAsync(TimeSpan.FromSeconds(20), Ct);

	private static async Task<ImportPreview> ReadAsync(string config)
	{
		OpenSshConfigImportSource source = new(() => Path.GetTempPath());
		return await source.ReadAsync(
			new ImportReadRequest(new ImportFile { Name = "config", Content = Encoding.UTF8.GetBytes(config) }),
			Ct);
	}
}
