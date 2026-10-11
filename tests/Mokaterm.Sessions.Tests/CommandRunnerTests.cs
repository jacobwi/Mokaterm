using System.Text;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Sessions.Commands;
using Mokaterm.Sessions.Protocols;
using Mokaterm.Sessions.Tests.Fakes;

namespace Mokaterm.Sessions.Tests;

public sealed class CommandRunnerTests : IAsyncDisposable
{
	private static readonly ProtocolDescriptor Shell = new()
	{
		Id = "cmd",
		DisplayName = "Shell",
		DefaultPort = 22,
		Capabilities = ProtocolCapabilities.Terminal | ProtocolCapabilities.Commands,
		AuthenticationMethods = [AuthenticationMethod.Password],
	};

	private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero));
	private readonly FakeConnectionRepository _repository = new();
	private readonly FakeCredentialStore _credentials = new();
	private readonly FakeUserInteraction _interaction = new();
	private readonly FakeKnownHostsStore _knownHosts = new();
	private readonly UncachedSettingsService _settings = new();
	private readonly ListLogger<CommandRunner> _logger = new();
	private readonly FakeProtocolProvider _shell = new(Shell);
	private readonly FakeProtocolProvider _sftp = new(TestProtocols.Sftp);
	private readonly FakeCommandFeature _commands = new();
	private readonly CountingVerifier _verifier = new();
	private readonly Lock _lock = new();
	private readonly List<FakeProtocolSession> _sessions = [];
	private readonly Guid _passwordId;
	private CommandRunner? _runner;

	public CommandRunnerTests()
	{
		_passwordId = _credentials.Add(
			new CredentialInfo { Id = Guid.NewGuid(), Name = "shared", Kind = CredentialKind.Password, IsShared = true },
			new CredentialSecretInput { Password = "pw" }).Id;
		_shell.OnConnect = ConnectAsync;
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private CommandRunner Runner => _runner ??= CreateRunner(_interaction);

	private IReadOnlyList<FakeProtocolSession> Sessions
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

	[Fact]
	public async Task StartAsync_RunsTheCommandOnEveryLogin_OverConnectionsOpenedOnlyForCommands()
	{
		ConnectionProfile[] logins = [AddLogin("10.0.0.1"), AddLogin("10.0.0.2"), AddLogin("10.0.0.3")];

		await using ICommandRun run = await StartAsync("uptime", logins);
		await run.WaitAsync(Ct);

		Assert.Equal("uptime", run.Command);
		Assert.Equal(["ops@10.0.0.1", "ops@10.0.0.2", "ops@10.0.0.3"], run.Hosts.Select(host => host.Title));
		Assert.All(run.Hosts, host =>
		{
			Assert.Equal(CommandHostState.Done, host.State);
			Assert.Equal(0, host.ExitCode);
			Assert.Equal("ok\n", host.Output.Text);
			Assert.Equal(1, host.Attempt);
			Assert.False(host.IsProblem);
		});

		Assert.All(_shell.Contexts, context => Assert.Equal(ConnectPurpose.Commands, context.Purpose));
		Assert.All(_commands.Requests, request => Assert.Equal("uptime", request.Command));
		Assert.All(_commands.Requests, request => Assert.Equal(256 * 1024, request.OutputLimitBytes));
		Assert.Equal(3, Sessions.Count);
		Assert.All(Sessions, session => Assert.True(session.IsDisposed));
		Assert.False(run.IsRunning);
	}

	[Fact]
	public async Task StartAsync_RefusesABlankCommand_AControlCharacter_AndNoLogins()
	{
		ConnectionProfile login = AddLogin("10.0.0.1");

		await Assert.ThrowsAsync<ArgumentException>(() => StartAsync("   ", [login]));
		await Assert.ThrowsAsync<ArgumentException>(() => StartAsync("echo \u0007", [login]));
		await Assert.ThrowsAsync<ArgumentException>(() => Runner.StartAsync(new CommandRunRequest { Command = "uptime", ConnectionIds = [] }, Ct));
		Assert.Equal(0, _shell.ConnectCount);
	}

	[Fact]
	public async Task ARun_NeverHasMoreHostsGoingThanTheParallelLimit()
	{
		ConnectionProfile[] logins = [.. Enumerable.Range(1, 5).Select(number => AddLogin($"10.0.0.{number}"))];
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_commands.OnRun = async (_, cancellationToken) =>
		{
			await release.Task.WaitAsync(cancellationToken);
			return FakeCommandFeature.Outcome(0, "ok\n");
		};

		await using ICommandRun run = await StartAsync("uptime", logins, new CommandRunSettings { ParallelHosts = 2 });
		await Eventually.TrueAsync(() => _commands.Running == 2, "two hosts run");
		await Eventually.StaysTrueAsync(() => _shell.ConnectCount == 2, "no third host connects while two are running");
		Assert.Equal(2, run.Hosts.Count(host => host.State == CommandHostState.Running));
		Assert.Equal(3, run.Hosts.Count(host => host.State == CommandHostState.Queued));

		release.SetResult();
		await run.WaitAsync(Ct);

		Assert.Equal(2, _commands.MostAtOnce);
		Assert.Equal(5, _shell.ConnectCount);
		Assert.All(run.Hosts, host => Assert.Equal(CommandHostState.Done, host.State));
	}

	[Fact]
	public async Task AHostThatRunsTooLong_FailsWithTheTimeout_AndItsConnectionCloses()
	{
		ConnectionProfile login = AddLogin("10.0.0.1");
		_commands.OnRun = async (_, cancellationToken) =>
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return FakeCommandFeature.Outcome(0, "");
		};

		await using ICommandRun run = await StartAsync("sleep 999", [login], new CommandRunSettings { TimeoutSeconds = 5 });
		await Eventually.TrueAsync(() => _commands.Running == 1, "the command runs");
		await Eventually.AdvanceUntilAsync(_time, TimeSpan.FromSeconds(1), () => run.Hosts[0].State == CommandHostState.Failed, "the host times out");

		Assert.Equal("The command did not finish within 5 s.", run.Hosts[0].Message);
		Assert.True(Assert.Single(Sessions).IsDisposed);
	}

	[Fact]
	public async Task TimeOnAPrompt_DoesNotCountAgainstTheHost()
	{
		GatedInteraction prompts = new();
		_runner = CreateRunner(prompts);
		ConnectionProfile login = AddLogin("10.0.0.1", savedPassword: false);

		await using ICommandRun run = await StartAsync("uptime", [login], new CommandRunSettings { TimeoutSeconds = 5, SkipHostsThatAsk = false });
		await Eventually.TrueAsync(() => prompts.Asked, "the login prompt opens");
		_time.Advance(TimeSpan.FromMinutes(1));
		await Eventually.StaysTrueAsync(() => run.Hosts[0].State == CommandHostState.Connecting, "the host waits on the user, not on the clock");

		prompts.Answer("ops", "pw");
		await run.WaitAsync(Ct);

		Assert.Equal(CommandHostState.Done, run.Hosts[0].State);
	}

	[Fact]
	public async Task CancelAll_StopsRunningAndQueuedHosts_AndClosesTheirConnections()
	{
		ConnectionProfile[] logins = [AddLogin("10.0.0.1"), AddLogin("10.0.0.2"), AddLogin("10.0.0.3")];
		_commands.OnRun = async (_, cancellationToken) =>
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return FakeCommandFeature.Outcome(0, "");
		};

		await using ICommandRun run = await StartAsync("uptime", logins, new CommandRunSettings { ParallelHosts = 1 });
		await Eventually.TrueAsync(() => _commands.Running == 1, "the first host runs");
		run.CancelAll();
		await run.WaitAsync(Ct);

		Assert.Equal(CommandHostState.Cancelled, run.Hosts[0].State);
		Assert.Equal("Cancelled.", run.Hosts[0].Message);
		Assert.All(run.Hosts.Skip(1), host => Assert.Equal("Cancelled before it started.", host.Message));
		Assert.Equal(1, _shell.ConnectCount);
		Assert.True(Assert.Single(Sessions).IsDisposed);
		Assert.False(run.IsRunning);
	}

	[Fact]
	public async Task SkippingHostsThatAsk_SkipsALoginWithNoSavedPassword_WithoutAsking()
	{
		ConnectionProfile login = AddLogin("10.0.0.1", savedPassword: false);

		await using ICommandRun run = await StartAsync("uptime", [login]);
		await run.WaitAsync(Ct);

		CommandHostResult host = Assert.Single(run.Hosts);
		Assert.Equal(CommandHostState.Skipped, host.State);
		Assert.Equal("It would ask for the login.", host.Message);
		Assert.Empty(_interaction.CredentialPrompts);
		Assert.Empty(Sessions);
	}

	[Fact]
	public async Task SkippingHostsThatAsk_SkipsAnUntrustedHostKey_BeforeTheVerifierCanAsk()
	{
		_knownHosts.Match = HostIdentityMatch.Unknown;
		ConnectionProfile login = AddLogin("10.0.0.1");

		await using ICommandRun run = await StartAsync("uptime", [login]);
		await run.WaitAsync(Ct);

		CommandHostResult host = Assert.Single(run.Hosts);
		Assert.Equal(CommandHostState.Skipped, host.State);
		Assert.Equal("It would ask about its host key, which is not trusted yet.", host.Message);
		Assert.Equal(0, _verifier.Calls);
	}

	[Theory]
	[InlineData(HostKeyPolicy.AcceptNew, CommandHostState.Done)]
	[InlineData(HostKeyPolicy.Strict, CommandHostState.Failed)]
	public async Task SkippingHostsThatAsk_LeavesAPolicyThatDoesNotAskToTheVerifier(HostKeyPolicy policy, CommandHostState expected)
	{
		_knownHosts.Match = HostIdentityMatch.Unknown;
		_settings.Set(new SecuritySettings { HostKeyPolicy = policy });
		_verifier.Answer = policy == HostKeyPolicy.AcceptNew;
		ConnectionProfile login = AddLogin("10.0.0.1");

		await using ICommandRun run = await StartAsync("uptime", [login]);
		await run.WaitAsync(Ct);

		Assert.Equal(expected, Assert.Single(run.Hosts).State);
		Assert.Equal(1, _verifier.Calls);
	}

	[Fact]
	public async Task WithAskingAllowed_ThePromptReachesTheWindow()
	{
		_interaction.AnswerCredentials("ops", "pw");
		ConnectionProfile login = AddLogin("10.0.0.1", savedPassword: false);

		await using ICommandRun run = await StartAsync("uptime", [login], new CommandRunSettings { SkipHostsThatAsk = false });
		await run.WaitAsync(Ct);

		Assert.Equal(CommandHostState.Done, Assert.Single(run.Hosts).State);
		_ = Assert.Single(_interaction.CredentialPrompts);
	}

	[Fact]
	public async Task RetrySkippedAsync_RunsTheSkippedHostsAgain_AskingThisTime()
	{
		ConnectionProfile asks = AddLogin("10.0.0.1", savedPassword: false);
		ConnectionProfile saved = AddLogin("10.0.0.2");
		await using ICommandRun run = await StartAsync("uptime", [asks, saved]);
		await run.WaitAsync(Ct);
		Assert.Equal(CommandHostState.Skipped, run.Hosts[0].State);

		_interaction.AnswerCredentials("ops", "pw");
		Assert.Equal(1, await run.RetrySkippedAsync(Ct));
		await run.WaitAsync(Ct);

		Assert.Equal(CommandHostState.Done, run.Hosts[0].State);
		Assert.Equal(2, run.Hosts[0].Attempt);
		Assert.Equal(1, run.Hosts[1].Attempt);
		_ = Assert.Single(_interaction.CredentialPrompts);
	}

	[Fact]
	public async Task RetryFailedAsync_RunsOnlyTheFailedHostsAgain()
	{
		ConnectionProfile flaky = AddLogin("10.0.0.1");
		ConnectionProfile steady = AddLogin("10.0.0.2");
		int flakyAttempts = 0;
		_shell.OnConnect = (context, cancellationToken) =>
			context.Host.Address == "10.0.0.1" && Interlocked.Increment(ref flakyAttempts) == 1
				? Task.FromException<IProtocolSession>(new ProtocolConnectException(ConnectFailure.HostUnreachable, "No route to 10.0.0.1."))
				: ConnectAsync(context, cancellationToken);

		await using ICommandRun run = await StartAsync("uptime", [flaky, steady]);
		await run.WaitAsync(Ct);
		Assert.Equal(CommandHostState.Failed, run.Hosts[0].State);
		Assert.Equal("No route to 10.0.0.1.", run.Hosts[0].Message);
		Assert.True(run.Hosts[0].IsProblem);

		Assert.Equal(1, await run.RetryFailedAsync(Ct));
		await run.WaitAsync(Ct);

		Assert.Equal(CommandHostState.Done, run.Hosts[0].State);
		Assert.Null(run.Hosts[0].Message);
		Assert.Equal(2, run.Hosts[0].Attempt);
		Assert.Equal(1, run.Hosts[1].Attempt);
		Assert.Equal(3, _shell.ConnectCount);
		Assert.Equal(0, await run.RetryFailedAsync(Ct));
	}

	[Fact]
	public async Task ANonZeroExit_IsDoneButCountsAsAProblem()
	{
		_commands.OnRun = (_, _) => Task.FromResult(FakeCommandFeature.Outcome(3, "", "no such unit\n"));
		ConnectionProfile login = AddLogin("10.0.0.1");

		await using ICommandRun run = await StartAsync("systemctl status nope", [login]);
		await run.WaitAsync(Ct);

		CommandHostResult host = Assert.Single(run.Hosts);
		Assert.Equal(CommandHostState.Done, host.State);
		Assert.Equal(3, host.ExitCode);
		Assert.Equal("no such unit\n", host.Error.Text);
		Assert.True(host.IsProblem);
	}

	[Fact]
	public async Task TheOutputCap_ReachesTheFeature_AndTheRowSaysAStreamWasCut()
	{
		string escape = ((char)0x1b).ToString();
		byte[] kept = Encoding.UTF8.GetBytes(new string('a', 4096));
		_commands.OnRun = (_, _) => Task.FromResult(new RemoteCommandOutcome
		{
			ExitCode = 0,
			Output = new CapturedOutput(kept, 1_000_000),
			Error = FakeCommandFeature.Captured($"{escape}[31merror{escape}[0m\r\nsecond\rthird"),
		});
		ConnectionProfile login = AddLogin("10.0.0.1");

		await using ICommandRun run = await StartAsync("cat big.log", [login], new CommandRunSettings { OutputKilobytes = 4 });
		await run.WaitAsync(Ct);

		Assert.Equal(4096, Assert.Single(_commands.Requests).OutputLimitBytes);
		CommandHostResult host = Assert.Single(run.Hosts);
		Assert.True(host.Output.IsTruncated);
		Assert.Equal(1_000_000, host.Output.TotalBytes);
		Assert.Equal(4096, host.Output.Text.Length);
		Assert.Equal("error\nsecond\nthird", host.Error.Text);
		Assert.False(host.Error.IsTruncated);
	}

	[Theory]
	[InlineData(new byte[] { 0x61, 0x62 }, 2)]
	[InlineData(new byte[] { 0x61, 0xC3 }, 1)]
	[InlineData(new byte[] { 0x61, 0xC3, 0xA9 }, 3)]
	[InlineData(new byte[] { 0x61, 0xE2, 0x82 }, 1)]
	[InlineData(new byte[] { 0xF0, 0x9F, 0x98 }, 0)]
	[InlineData(new byte[] { 0xF0, 0x9F, 0x98, 0x80 }, 4)]
	public void CompleteLength_DropsOnlyACharacterTheCapCutInHalf(byte[] bytes, int expected) =>
		Assert.Equal(expected, CommandOutputDecoder.CompleteLength(bytes));

	[Fact]
	public async Task ALoginThatCannotRunCommands_FailsWithTheReason_AndNeverConnects()
	{
		ConnectionProfile files = AddLogin("10.0.0.1", protocolId: TestProtocols.Sftp.Id);
		Guid deleted = Guid.NewGuid();

		await using ICommandRun run = await Runner.StartAsync(new CommandRunRequest { Command = "uptime", ConnectionIds = [files.Id, deleted] }, Ct);
		await run.WaitAsync(Ct);

		Assert.Equal(CommandHostState.Failed, run.Hosts[0].State);
		Assert.Equal(CommandRunEligibility.WhyNot(TestProtocols.Sftp), run.Hosts[0].Message);
		Assert.Equal(CommandHostState.Failed, run.Hosts[1].State);
		Assert.Equal("This login no longer exists.", run.Hosts[1].Message);
		Assert.Equal("Deleted login", run.Hosts[1].Title);
		Assert.Equal(0, _sftp.ConnectCount);
		Assert.Equal(0, _shell.ConnectCount);
	}

	[Theory]
	[InlineData(Ending.Done)]
	[InlineData(Ending.CommandFailed)]
	[InlineData(Ending.NoFeature)]
	[InlineData(Ending.Cancelled)]
	[InlineData(Ending.RunDisposed)]
	[InlineData(Ending.RunnerDisposed)]
	public async Task EveryConnectionARunOpens_IsClosed_HoweverItsHostEnds(Ending ending)
	{
		ConnectionProfile login = AddLogin("10.0.0.1");
		if (ending == Ending.NoFeature)
		{
			_shell.OnConnect = (_, _) => Task.FromResult<IProtocolSession>(Track(new FakeProtocolSession()));
		}

		_commands.OnRun = ending switch
		{
			Ending.Done => (_, _) => Task.FromResult(FakeCommandFeature.Outcome(0, "ok\n")),
			Ending.CommandFailed => (_, _) => Task.FromException<RemoteCommandOutcome>(new RemoteCommandException("The connection closed before the command finished.")),
			_ => async (_, cancellationToken) =>
			{
				await Task.Delay(Timeout.Infinite, cancellationToken);
				return FakeCommandFeature.Outcome(0, "");
			},
		};

		ICommandRun run = await StartAsync("uptime", [login]);
		await Eventually.TrueAsync(() => Sessions.Count == 1, "the host connects");
		switch (ending)
		{
			case Ending.Cancelled:
				await Eventually.TrueAsync(() => _commands.Running == 1, "the command runs");
				run.CancelAll();
				await run.WaitAsync(Ct);
				break;
			case Ending.RunDisposed:
				await Eventually.TrueAsync(() => _commands.Running == 1, "the command runs");
				await run.DisposeAsync();
				break;
			case Ending.RunnerDisposed:
				await Eventually.TrueAsync(() => _commands.Running == 1, "the command runs");
				await Runner.DisposeAsync();
				break;
			default:
				await run.WaitAsync(Ct);
				break;
		}

		await Eventually.TrueAsync(() => Sessions[0].IsDisposed, "the connection is closed");
		CommandHostResult host = Assert.Single(run.Hosts);
		Assert.Equal(
			ending switch
			{
				Ending.Done => CommandHostState.Done,
				Ending.CommandFailed or Ending.NoFeature => CommandHostState.Failed,
				_ => CommandHostState.Cancelled,
			},
			host.State);
		await run.DisposeAsync();
	}

	[Fact]
	public async Task DisposingTheRunner_RefusesNewRuns()
	{
		ConnectionProfile login = AddLogin("10.0.0.1");
		await Runner.DisposeAsync();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => StartAsync("uptime", [login]));
	}

	[Fact]
	public async Task NothingTheCommandSaysReachesTheLog()
	{
		const string Token = "tok3n-s3cr3t";
		_commands.OnRun = (_, _) => Task.FromException<RemoteCommandOutcome>(new InvalidOperationException($"curl -H {Token} broke"));
		ConnectionProfile login = AddLogin("10.0.0.1");

		await using ICommandRun run = await StartAsync($"curl -H 'Authorization: {Token}' https://example.test", [login]);
		await run.WaitAsync(Ct);

		CommandHostResult host = Assert.Single(run.Hosts);
		Assert.Equal(CommandHostState.Failed, host.State);
		Assert.DoesNotContain(Token, host.Message, StringComparison.Ordinal);
		Assert.NotEmpty(_logger.Entries);
		Assert.All(_logger.Entries, entry => Assert.DoesNotContain(Token, entry.Message, StringComparison.Ordinal));
		Assert.DoesNotContain(Token, new CommandRunRequest { Command = Token, ConnectionIds = [] }.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain(Token, new RemoteCommandRequest { Command = Token }.ToString(), StringComparison.Ordinal);
	}

	public enum Ending
	{
		Done,
		CommandFailed,
		NoFeature,
		Cancelled,
		RunDisposed,
		RunnerDisposed,
	}

	private static HostIdentity Identity(ProtocolConnectContext context) => new()
	{
		Host = context.Host.Address,
		Port = context.Port,
		Kind = HostIdentityKind.SshHostKey,
		Algorithm = "ssh-ed25519",
		Fingerprint = "SHA256:test",
	};

	private CommandRunner CreateRunner(IUserInteraction interaction) => new(
		_repository,
		_credentials,
		new ProtocolRegistry([_shell, _sftp]),
		_verifier,
		_knownHosts,
		_settings,
		interaction,
		_logger,
		_time);

	private Task<ICommandRun> StartAsync(string command, IEnumerable<ConnectionProfile> logins, CommandRunSettings? options = null) =>
		Runner.StartAsync(
			new CommandRunRequest
			{
				Command = command,
				ConnectionIds = [.. logins.Select(login => login.Id)],
				Options = options ?? new CommandRunSettings(),
			},
			Ct);

	private ConnectionProfile AddLogin(string address, bool savedPassword = true, string protocolId = "cmd")
	{
		HostProfile host = new() { Id = Guid.NewGuid(), Address = address };
		ConnectionProfile login = new()
		{
			Id = Guid.NewGuid(),
			HostId = host.Id,
			ProtocolId = protocolId,
			Username = "ops",
			AuthenticationMethod = AuthenticationMethod.Password,
			CredentialId = savedPassword ? _passwordId : null,
		};

		_repository.Add(host, login);
		return login;
	}

	/// <summary>What a provider does: log in, check the host, hand back a session that runs commands.</summary>
	private async Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		using LoginCredentials login = await context.Credentials.GetAsync(cancellationToken)
			?? throw new ProtocolConnectException(ConnectFailure.Cancelled, "The login was cancelled.");
		if (!await context.HostVerifier.VerifyAsync(Identity(context), cancellationToken))
		{
			throw new ProtocolConnectException(ConnectFailure.HostIdentityRejected, "The host key was not trusted.");
		}

		return Track(new FakeProtocolSession { Features = [_commands] });
	}

	private FakeProtocolSession Track(FakeProtocolSession session)
	{
		lock (_lock)
		{
			_sessions.Add(session);
		}

		return session;
	}

	private sealed class CountingVerifier : IHostIdentityVerifier
	{
		private int _calls;

		public int Calls => Volatile.Read(ref _calls);

		public bool Answer { get; set; } = true;

		public ValueTask<bool> VerifyAsync(HostIdentity identity, CancellationToken cancellationToken = default)
		{
			_ = Interlocked.Increment(ref _calls);
			return ValueTask.FromResult(Answer);
		}
	}

	/// <summary>A window where the user takes their time over the login prompt.</summary>
	private sealed class GatedInteraction : IUserInteraction
	{
		private readonly TaskCompletionSource<CredentialPromptResult?> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _asked;

		public bool Asked => Volatile.Read(ref _asked) > 0;

		public void Answer(string username, string password) => _answer.SetResult(new CredentialPromptResult(username, password, Save: false));

		public Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default)
		{
			_ = Interlocked.Increment(ref _asked);
			return _answer.Task.WaitAsync(cancellationToken);
		}

		public Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default) =>
			Task.FromResult(HostTrustDecision.AcceptOnce);

		public Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default) =>
			Task.FromResult<SecretPromptResult?>(null);

		public Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<string>?>(null);

		public Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default) => Task.FromResult(ConfirmResult.No);

		public Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default) =>
			Task.FromResult(OverwriteDecision.Cancel);

		public void Notify(NoticeSeverity severity, string message, string? title = null)
		{
		}
	}
}
