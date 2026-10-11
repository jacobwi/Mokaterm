using System.Security.Cryptography;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Ftp.Tests.Loopback;

/// <summary>Continuing transfers with APPE and REST, and asking the server for a checksum, against a server that speaks them.</summary>
public sealed class FtpResumeTests
{
	private const string Target = "/home/alice/big.bin";

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Upload_AfterAnAttemptLeftPartOfTheFile_AppendsOnlyTheRest()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		byte[] payload = RandomNumberGenerator.GetBytes(200_000);
		server.Files.AddFile(Target, payload[..70_000]);
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);
		long? resumedAt = null;
		UploadResume resume = new(offset => resumedAt = offset);
		resume.Record(Target, isTemporary: false);
		RecordingProgress<long> progress = new();

		await files.UploadAsync(Target, new MemoryStream(payload), new UploadOptions { Overwrite = true, Resume = resume }, progress, cancellationToken);

		Assert.Equal(payload, server.Files.Get(Target)?.Content);
		Assert.Equal(70_000, resumedAt);
		Assert.Equal(130_000, progress.Reports.Last());
		Assert.Contains(server.Commands, command => command.StartsWith("APPE ", StringComparison.Ordinal));
		Assert.Null(resume.PartialPath);
	}

	// FTP writes in place, so the file an attempt leaves behind is the target itself, and it is recorded before the first
	// byte: a failure further on still knows what to continue.
	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Upload_ThatFails_LeavesTheTargetRecordedAsItsPartialFile()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);
		UploadResume resume = new();

		await Assert.ThrowsAsync<RemoteFileSystemException>(async () =>
			await files.UploadAsync("/home/alice/missing/big.bin", new MemoryStream(new byte[10]), new UploadOptions { Resume = resume }, null, cancellationToken));

		Assert.Equal("/home/alice/missing/big.bin", resume.PartialPath);
		Assert.False(resume.IsTemporary);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Upload_OfAFileShorterThanWhatIsAlreadyThere_FailsAndForgetsThePartialFile()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		server.Files.AddFile(Target, new byte[5_000]);
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);
		UploadResume resume = new();
		resume.Record(Target, isTemporary: false);

		await Assert.ThrowsAsync<RemoteFileSystemException>(async () =>
			await files.UploadAsync(Target, new MemoryStream(new byte[1_000]), new UploadOptions { Overwrite = true, Resume = resume }, null, cancellationToken));

		Assert.Null(resume.PartialPath);
		Assert.DoesNotContain(server.Commands, command => command.StartsWith("APPE ", StringComparison.Ordinal));
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Download_FromAnOffset_ReceivesOnlyTheRest()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		byte[] payload = RandomNumberGenerator.GetBytes(150_000);
		server.Files.AddFile(Target, payload);
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);
		using MemoryStream received = new();
		RecordingProgress<long> progress = new();

		await files.DownloadAsync(Target, received, 100_000, progress, cancellationToken);

		Assert.Equal(payload[100_000..], received.ToArray());
		Assert.Equal(50_000, progress.Reports.Last());
		Assert.Contains("REST 100000", server.Commands);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Download_FromPastTheEnd_IsRefusedAsAChangedFile()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		server.Files.AddFile(Target, new byte[1_000]);
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(async () =>
			await files.DownloadAsync(Target, new MemoryStream(), 5_000, null, cancellationToken));

		Assert.Contains("changed since the last attempt", failure.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Checksum_FromAServerWithXSha256_IsItsSha256InLowerCase()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer(new LoopbackFtpServerOptions { SupportsXSha256 = true });
		byte[] payload = RandomNumberGenerator.GetBytes(40_000);
		server.Files.AddFile(Target, payload);
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		string? checksum = await files.ComputeChecksumAsync(Target, cancellationToken);

		Assert.Equal(ChecksumAlgorithm.Sha256, files.ServerChecksum);
		Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(payload)), checksum);
	}

	[Fact(Timeout = FtpHarness.TestTimeoutMilliseconds)]
	public async Task Checksum_FromAServerWithoutAHashCommand_IsNotOffered()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackFtpServer server = FtpHarness.StartServer();
		await using IProtocolSession session = await FtpHarness.ConnectAsync(server, cancellationToken);
		IRemoteFileSystem files = await FtpHarness.OpenFileSystemAsync(session, cancellationToken);

		Assert.Null(files.ServerChecksum);
		Assert.Null(await files.ComputeChecksumAsync(FtpHarness.NotesPath, cancellationToken));
	}
}
