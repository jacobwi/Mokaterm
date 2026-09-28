using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ftp.Tests.Fakes;

namespace Mokaterm.Modules.Ftp.Tests.Loopback;

/// <summary>Builds servers, providers and connect contexts for the loopback tests.</summary>
internal static class FtpHarness
{
	public const string User = "alice";

	public const string Password = "s3cret";

	public const string Home = "/home/alice";

	public const string NotesPath = "/home/alice/notes.txt";

	public const string NotesText = "hello from ftp";

	public const int TestTimeoutMilliseconds = TestTimeouts.NetworkMilliseconds;

	/// <summary>Short enough that a "slow" verifier in a test takes the reconnect path.</summary>
	public static readonly TimeSpan CertificateDecisionWait = TimeSpan.FromMilliseconds(300);

	public static LoopbackFtpServer StartServer(LoopbackFtpServerOptions? options = null)
	{
		options ??= new LoopbackFtpServerOptions();
		options.Users[User] = new LoopbackFtpUser(Password, Home);
		options.Files
			.AddDirectory(Home)
			.AddFile(NotesPath, NotesText)
			.AddFile("/home/alice/.profile", "export PS1='$ '")
			.AddDirectory("/home/alice/www")
			.AddFile("/home/alice/www/index.html", "<h1>hi</h1>")
			.AddLink("/home/alice/site", "www")
			.AddLink("/home/alice/readme", "notes.txt")
			.AddDirectory("/home/bob")
			.AddDirectory("/pub")
			.AddFile("/pub/welcome.txt", "anonymous hello");
		return new LoopbackFtpServer(options);
	}

	public static FtpProtocolProvider CreateProvider(FakeSettingsService? settings = null, TimeProvider? timeProvider = null) =>
		new(settings ?? new FakeSettingsService(), timeProvider ?? TimeProvider.System, NullLoggerFactory.Instance)
		{
			CertificateDecisionWait = CertificateDecisionWait,
		};

	public static ProtocolConnectContext CreateContext(
		int port,
		ICredentialSource? credentials = null,
		IHostIdentityVerifier? verifier = null,
		ProtocolOptions? options = null,
		RecordingProgress<string>? status = null,
		string address = "127.0.0.1",
		bool withoutStatus = false) =>
		new ConnectContext
		{
			ProtocolId = FtpProtocolProvider.ProtocolId,
			Port = port,
			Address = address,
			Credentials = credentials ?? new FakeCredentialSource(User, Password),
			Verifier = verifier ?? FakeHostVerifier.Accepting(),
			Options = options ?? ProtocolOptions.Empty,
			Status = status,
			WithoutStatus = withoutStatus,
		}.Build();

	public static async Task<IProtocolSession> ConnectAsync(LoopbackFtpServer server, CancellationToken cancellationToken, FtpProtocolProvider? provider = null) =>
		await (provider ?? CreateProvider()).ConnectAsync(CreateContext(server.Port), cancellationToken);

	public static IFileSystemFeature FileSystemFeature(IProtocolSession session)
	{
		IFileSystemFeature? feature = session.GetFeature<IFileSystemFeature>();
		Assert.NotNull(feature);
		return feature;
	}

	public static async Task<IRemoteFileSystem> OpenFileSystemAsync(IProtocolSession session, CancellationToken cancellationToken) =>
		await FileSystemFeature(session).OpenAsync(cancellationToken);
}
