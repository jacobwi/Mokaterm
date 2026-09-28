using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Protocols;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Mokaterm.Modules.Ssh.Tunnels;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// Talks to a real server named by <c>MOKATERM_TEST_SSH</c> (<c>user:password@host:port</c>). Skipped when it is not set.
/// </summary>
public sealed class SshIntegrationTests
{
	private const string ServerVariable = "MOKATERM_TEST_SSH";

	[Fact]
	public async Task Ssh_ConnectsOpensAShellAndRoundTripsAFile()
	{
		LiveEndpoint server = RequireServer();
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		timeout.CancelAfter(TimeSpan.FromMinutes(2));
		CancellationToken token = timeout.Token;

		FakeHostVerifier verifier = FakeHostVerifier.Accepting();
		SshConnector connector = new(new FakeSettingsService());
		IProtocolSession session = await new SshProtocolProvider(connector).ConnectAsync(CreateContext(server, SshProtocolIds.Ssh, verifier), token);
		await using (session)
		{
			HostIdentity identity = Assert.Single(verifier.Seen);
			Assert.Equal(HostIdentityKind.SshHostKey, identity.Kind);
			Assert.StartsWith("SHA256:", identity.Fingerprint, StringComparison.Ordinal);

			ITerminalChannel terminal = session.GetFeature<ITerminalChannel>() ?? throw new InvalidOperationException("No terminal.");
			await terminal.WriteAsync(Encoding.UTF8.GetBytes("echo mokaterm-$((6*7))\n"), token);
			string output = await ReadUntilAsync(terminal, "mokaterm-42", token);
			Assert.Contains("mokaterm-42", output, StringComparison.Ordinal);
			await terminal.ResizeAsync(new TerminalSize(100, 30), token);

			IFileSystemFeature files = session.GetFeature<IFileSystemFeature>() ?? throw new InvalidOperationException("No file system.");
			IRemoteFileSystem fileSystem = await files.OpenAsync(token);
			await RoundTripFileAsync(fileSystem, token);
		}

		await session.Completion;
	}

	[Fact]
	public async Task Sftp_ConnectsAndListsTheHomeDirectory()
	{
		LiveEndpoint server = RequireServer();
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		timeout.CancelAfter(TimeSpan.FromMinutes(2));
		CancellationToken token = timeout.Token;

		SshConnector connector = new(new FakeSettingsService());
		IProtocolSession session = await new SftpProtocolProvider(connector).ConnectAsync(CreateContext(server, SshProtocolIds.Sftp, FakeHostVerifier.Accepting()), token);
		await using (session)
		{
			Assert.Null(session.GetFeature<ITerminalChannel>());
			IRemoteFileSystem fileSystem = await (session.GetFeature<IFileSystemFeature>() ?? throw new InvalidOperationException("No file system.")).OpenAsync(token);
			string home = await fileSystem.GetHomeDirectoryAsync(token);

			Assert.StartsWith("/", home, StringComparison.Ordinal);
			Assert.All(await fileSystem.ListAsync(home, token), entry => Assert.Equal(RemotePath.Combine(home, entry.Name), entry.Path));
		}

		await session.Completion;
	}

	[Fact]
	public async Task Ssh_WrongPassword_FailsAsAuthenticationFailure()
	{
		LiveEndpoint server = RequireServer();
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		timeout.CancelAfter(TimeSpan.FromMinutes(1));

		LiveEndpoint wrong = server with { Password = server.Password + "-wrong" };
		SshConnector connector = new(new FakeSettingsService());

		ProtocolConnectException exception = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => new SshProtocolProvider(connector).ConnectAsync(CreateContext(wrong, SshProtocolIds.Ssh, FakeHostVerifier.Accepting()), timeout.Token));

		Assert.Equal(ConnectFailure.AuthenticationFailed, exception.Failure);
	}

	[Fact]
	public async Task Ssh_ReachesTheServerThroughItselfAsAJumpHost()
	{
		LiveEndpoint server = RequireServer();
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		timeout.CancelAfter(TimeSpan.FromMinutes(2));
		CancellationToken token = timeout.Token;

		// The same host twice: hop one dials it directly and forwards a loopback port to it, and the session logs in
		// again through that port, which is the whole of ProxyJump without needing a second machine.
		FakeConnectionResolver resolver = new();
		(string user, string password) = server.RequireLogin(ServerVariable);
		Guid jumpHost = resolver.Add(server.Host, server.Port, user, password);
		ProtocolConnectContext direct = CreateContext(server, SshProtocolIds.Ssh, FakeHostVerifier.Accepting());
		ProtocolConnectContext context = new()
		{
			SessionId = direct.SessionId,
			Host = direct.Host,
			Connection = direct.Connection with
			{
				Options = ProtocolOptions.Empty.With(SshConnectionOptions.JumpHostsKey, jumpHost.ToString("D")),
			},
			Port = direct.Port,
			Credentials = direct.Credentials,
			HostVerifier = direct.HostVerifier,
			Interaction = direct.Interaction,
			Resolver = resolver,
		};

		SshConnector connector = new(new FakeSettingsService());
		IProtocolSession session = await new SshProtocolProvider(connector).ConnectAsync(context, token);
		await using (session)
		{
			ITerminalChannel terminal = session.GetFeature<ITerminalChannel>() ?? throw new InvalidOperationException("No terminal.");
			await terminal.WriteAsync(Encoding.UTF8.GetBytes("echo mokaterm-jump-$((6*7))\n"), token);
			Assert.Contains("mokaterm-jump-42", await ReadUntilAsync(terminal, "mokaterm-jump-42", token), StringComparison.Ordinal);

			ISshTunnelFeature tunnels = session.GetFeature<ISshTunnelFeature>() ?? throw new InvalidOperationException("No tunnels.");
			SshTunnelStatus opened = await tunnels.AddAsync(
				SshTunnelDefinition.Create(SshTunnelKind.Local) with
				{
					ListenPort = 0,
					DestinationHost = "127.0.0.1",
					DestinationPort = server.Port,
				},
				token);

			Assert.Equal(SshTunnelState.Open, opened.State);
			Assert.NotNull(opened.BoundPort);
			Assert.Equal(1, tunnels.OpenCount);
			Assert.Equal(SshTunnelState.Closed, (await tunnels.CloseAsync(opened.Tunnel.Id, token)).State);
		}
	}

	private static async Task RoundTripFileAsync(IRemoteFileSystem fileSystem, CancellationToken token)
	{
		string home = await fileSystem.GetHomeDirectoryAsync(token);
		string path = RemotePath.Combine(home, $".mokaterm-test-{Guid.NewGuid():N}.bin");
		string renamed = path + ".renamed";
		byte[] content = RandomNumberGenerator.GetBytes(300_000);
		DateTimeOffset modified = new(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);
		try
		{
			long reported = 0;
			using (MemoryStream source = new(content))
			{
				UploadOptions options = new() { Permissions = UnixFileMode.UserRead | UnixFileMode.UserWrite, LastModified = modified };
				await fileSystem.UploadAsync(path, source, options, new SynchronousProgress(bytes => reported = bytes), token);
			}

			Assert.Equal(content.Length, reported);

			RemoteFileEntry uploaded = await fileSystem.StatAsync(path, token) ?? throw new InvalidOperationException("The upload is missing.");
			Assert.Equal(RemoteEntryKind.File, uploaded.Kind);
			Assert.Equal(content.Length, uploaded.Size);
			Assert.Equal("600", UnixFileModeFormat.ToOctal(uploaded.Permissions ?? UnixFileMode.None));
			Assert.Equal(modified, uploaded.LastModified);
			Assert.Contains(await fileSystem.ListAsync(home, token), entry => entry.Path == path);

			await Assert.ThrowsAsync<RemoteFileSystemException>(async () =>
			{
				using MemoryStream again = new(content);
				await fileSystem.UploadAsync(path, again, UploadOptions.Default, null, token);
			});

			await fileSystem.RenameAsync(path, renamed, overwrite: false, token);
			Assert.Null(await fileSystem.StatAsync(path, token));

			using MemoryStream downloaded = new();
			await fileSystem.DownloadAsync(renamed, downloaded, null, token);
			Assert.Equal(content, downloaded.ToArray());
		}
		finally
		{
			foreach (string leftover in new[] { path, renamed })
			{
				if (await fileSystem.StatAsync(leftover, CancellationToken.None) is not null)
				{
					await fileSystem.DeleteAsync(leftover, recursive: false, CancellationToken.None);
				}
			}
		}

		Assert.Null(await fileSystem.StatAsync(renamed, token));
	}

	private static async Task<string> ReadUntilAsync(ITerminalChannel terminal, string expected, CancellationToken token)
	{
		StringBuilder output = new();
		byte[] buffer = new byte[4096];
		while (!output.ToString().Contains(expected, StringComparison.Ordinal))
		{
			int read = await terminal.ReadAsync(buffer, token);
			if (read == 0)
			{
				break;
			}

			output.Append(Encoding.UTF8.GetString(buffer, 0, read));
		}

		return output.ToString();
	}

	private static ProtocolConnectContext CreateContext(LiveEndpoint server, string protocolId, IHostIdentityVerifier verifier)
	{
		(string user, string password) = server.RequireLogin(ServerVariable);
		HostProfile host = new() { Id = Guid.NewGuid(), Address = server.Host };
		return new ProtocolConnectContext
		{
			SessionId = Guid.NewGuid(),
			Host = host,
			Connection = new ConnectionProfile
			{
				Id = Guid.NewGuid(),
				HostId = host.Id,
				ProtocolId = protocolId,
				Port = server.Port,
				Username = user,
				AuthenticationMethod = AuthenticationMethod.Password,
			},
			Port = server.Port,
			Credentials = new PasswordCredentialSource(user, password),
			HostVerifier = verifier,
			Interaction = new DismissingInteraction(),
		};
	}

	private static LiveEndpoint RequireServer()
	{
		string value = LiveEndpoint.Require(
			ServerVariable,
			$"Set {ServerVariable}=user:password@host:port to run tests against a real SSH server.");
		LiveEndpoint server = LiveEndpoint.Parse(value, SshProtocolProvider.SshDescriptor.DefaultPort);

		// SSH cannot log in without a user and a password, so a value that names neither is refused here rather than
		// halfway through a connect.
		_ = server.RequireLogin(ServerVariable);
		return server;
	}

	private sealed class SynchronousProgress(Action<long> report) : IProgress<long>
	{
		public void Report(long value) => report(value);
	}
}
