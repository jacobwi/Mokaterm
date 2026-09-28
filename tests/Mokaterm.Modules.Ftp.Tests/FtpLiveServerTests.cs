using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ftp.Tests.Fakes;
using Mokaterm.Modules.Ftp.Tests.Loopback;

namespace Mokaterm.Modules.Ftp.Tests;

/// <summary>
/// Runs against a real server when MOKATERM_TEST_FTP is set to <c>user:password@host:port</c>, optionally followed by
/// <c>?encryption=Explicit</c> or <c>?encryption=Implicit</c>. Percent-encode special characters in the password.
/// </summary>
public sealed class FtpLiveServerTests
{
	private const string Variable = "MOKATERM_TEST_FTP";

	private const long MaxDownloadBytes = 1024 * 1024;

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ListHome_DownloadSmallFile()
	{
		string setting = LiveEndpoint.Require(
			Variable,
			$"Set {Variable}=user:password@host:port[?encryption=Explicit] to run against a real FTP server.");

		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		LiveEndpoint target = LiveEndpoint.Parse(setting, FtpProtocolProvider.DefaultPort);
		FtpEncryption encryption = target.Option("encryption", FtpEncryption.None);

		// Implicit FTPS answers on 990, so the value only has to name a port when it is neither default.
		int port = target.PortWasNamed ? target.Port
			: encryption == FtpEncryption.Implicit ? FtpConnectionOptions.ImplicitTlsPort
			: FtpProtocolProvider.DefaultPort;
		FakeHostVerifier verifier = FakeHostVerifier.Accepting();
		ProtocolConnectContext context = FtpHarness.CreateContext(
			port,
			new FakeCredentialSource(target.User, target.Password),
			verifier,
			new FtpConnectionOptions { Encryption = encryption }.ApplyTo(ProtocolOptions.Empty),
			address: target.Host);
		FtpProtocolProvider provider = new(new FakeSettingsService(), TimeProvider.System, NullLoggerFactory.Instance);

		await using IProtocolSession session = await provider.ConnectAsync(context, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);
		string home = await files.GetHomeDirectoryAsync(cancellationToken);
		IReadOnlyList<RemoteFileEntry> entries = await files.ListAsync(home, cancellationToken);

		Assert.Equal(encryption != FtpEncryption.None, !verifier.Seen.IsEmpty);
		RemoteFileEntry? small = entries
			.Where(entry => entry.Kind == RemoteEntryKind.File && entry.Size is > 0 and <= MaxDownloadBytes)
			.OrderBy(entry => entry.Size)
			.FirstOrDefault();
		if (small is null)
		{
			Assert.Skip($"Connected and listed {home}, but it holds no file between 1 byte and 1 MB to download.");
		}

		using MemoryStream downloaded = new();
		RecordingProgress<long> progress = new();
		await files.DownloadAsync(small.Path, downloaded, progress, cancellationToken);

		Assert.Equal(small.Size, downloaded.Length);
		Assert.Equal(small.Size, progress.Reports.Last());
	}
}
