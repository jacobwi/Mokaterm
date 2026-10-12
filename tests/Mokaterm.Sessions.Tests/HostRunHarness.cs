using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Sessions.Commands;
using Mokaterm.Sessions.Protocols;
using Mokaterm.Sessions.Tests.Fakes;

namespace Mokaterm.Sessions.Tests;

/// <summary>
/// A runner over fake stores, for both kinds of run: its protocol logs in, checks the host key and hands back a session
/// that runs commands and reads its machine, and every session it hands out is kept to check it was closed.
/// </summary>
internal sealed class HostRunHarness : IAsyncDisposable
{
	public static readonly ProtocolDescriptor Shell = new()
	{
		Id = "cmd",
		DisplayName = "Shell",
		DefaultPort = 22,
		Capabilities = ProtocolCapabilities.Terminal | ProtocolCapabilities.Commands | ProtocolCapabilities.HostSummary,
		AuthenticationMethods = [AuthenticationMethod.Password],
	};

	/// <summary>Runs commands but cannot say what its machine is.</summary>
	public static readonly ProtocolDescriptor CommandsOnly = Shell with { Id = "cmdonly", DisplayName = "Commands only", Capabilities = ProtocolCapabilities.Commands };

	private readonly Lock _lock = new();
	private readonly List<FakeProtocolSession> _sessions = [];
	private HostRunner? _runner;

	public HostRunHarness()
	{
		PasswordId = Credentials.Add(
			new CredentialInfo { Id = Guid.NewGuid(), Name = "shared", Kind = CredentialKind.Password, IsShared = true },
			new CredentialSecretInput { Password = "pw" }).Id;
		ShellProvider.OnConnect = ConnectAsync;
		CommandsOnlyProvider.OnConnect = ConnectAsync;
	}

	public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero));

	public FakeConnectionRepository Repository { get; } = new();

	public FakeCredentialStore Credentials { get; } = new();

	public FakeUserInteraction Interaction { get; } = new();

	public FakeKnownHostsStore KnownHosts { get; } = new();

	public UncachedSettingsService Settings { get; } = new();

	public ListLogger<HostRunner> Logger { get; } = new();

	public FakeProtocolProvider ShellProvider { get; } = new(Shell);

	public FakeProtocolProvider CommandsOnlyProvider { get; } = new(CommandsOnly);

	public FakeProtocolProvider SftpProvider { get; } = new(TestProtocols.Sftp);

	public FakeCommandFeature Commands { get; } = new();

	public FakeSummaryFeature Summaries { get; } = new();

	public CountingVerifier Verifier { get; } = new();

	public Guid PasswordId { get; }

	/// <summary>The runner the tests use; created on first use with the harness's own prompts, or set to one made with others.</summary>
	public HostRunner Runner
	{
		get => _runner ??= CreateRunner(Interaction);
		set => _runner = value;
	}

	public IReadOnlyList<FakeProtocolSession> Sessions
	{
		get
		{
			lock (_lock)
			{
				return [.. _sessions];
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_runner is not null)
		{
			await _runner.DisposeAsync();
		}
	}

	public HostRunner CreateRunner(IUserInteraction interaction) => new(
		Repository,
		Credentials,
		new ProtocolRegistry([ShellProvider, CommandsOnlyProvider, SftpProvider]),
		Verifier,
		KnownHosts,
		Settings,
		interaction,
		Logger,
		Time);

	public ConnectionProfile AddLogin(string address, bool savedPassword = true, string protocolId = "cmd")
	{
		HostProfile host = new() { Id = Guid.NewGuid(), Address = address };
		ConnectionProfile login = new()
		{
			Id = Guid.NewGuid(),
			HostId = host.Id,
			ProtocolId = protocolId,
			Username = "ops",
			AuthenticationMethod = AuthenticationMethod.Password,
			CredentialId = savedPassword ? PasswordId : null,
		};

		Repository.Add(host, login);
		return login;
	}

	/// <summary>What a provider does: log in, check the host, hand back a session that runs commands and reads its machine.</summary>
	public async Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		using LoginCredentials login = await context.Credentials.GetAsync(cancellationToken)
			?? throw new ProtocolConnectException(ConnectFailure.Cancelled, "The login was cancelled.");
		if (!await context.HostVerifier.VerifyAsync(Identity(context), cancellationToken))
		{
			throw new ProtocolConnectException(ConnectFailure.HostIdentityRejected, "The host key was not trusted.");
		}

		return Track(new FakeProtocolSession { Features = [Commands, Summaries] });
	}

	public FakeProtocolSession Track(FakeProtocolSession session)
	{
		lock (_lock)
		{
			_sessions.Add(session);
		}

		return session;
	}

	private static HostIdentity Identity(ProtocolConnectContext context) => new()
	{
		Host = context.Host.Address,
		Port = context.Port,
		Kind = HostIdentityKind.SshHostKey,
		Algorithm = "ssh-ed25519",
		Fingerprint = "SHA256:test",
	};
}
