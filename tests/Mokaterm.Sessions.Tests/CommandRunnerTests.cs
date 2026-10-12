using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Sessions.Tests.Fakes;

namespace Mokaterm.Sessions.Tests;

/// <summary>A command run through the pipeline it shares with surveys: <see cref="HostRunHarness"/> holds the runner and its fakes.</summary>
public sealed class CommandRunnerTests : IAsyncDisposable
{
	private readonly HostRunHarness _harness = new();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	public ValueTask DisposeAsync() => _harness.DisposeAsync();

	[Fact]
	public async Task StartAsync_RunsTheCommandOnEveryLogin_OverConnectionsOpenedOnlyForCommands()
	{
		ConnectionProfile[] logins = [_harness.AddLogin("10.0.0.1"), _harness.AddLogin("10.0.0.2"), _harness.AddLogin("10.0.0.3")];

		await using ICommandRun run = await StartAsync("uptime", logins);
		await run.WaitAsync(Ct);

		Assert.Equal("uptime", run.Command);
		Assert.Equal(["ops@10.0.0.1", "ops@10.0.0.2", "ops@10.0.0.3"], run.Hosts.Select(host => host.Title));
		Assert.All(run.Hosts, host =>
		{
			Assert.Equal(HostRunState.Done, host.State);
			Assert.Equal(0, host.ExitCode);
			Assert.Equal("ok\n", host.Output.Text);
			Assert.Equal(1, host.Attempt);
			Assert.False(host.IsProblem);
		});

		Assert.All(_harness.ShellProvider.Contexts, context => Assert.Equal(ConnectPurpose.Commands, context.Purpose));
		Assert.All(_harness.Commands.Requests, request => Assert.Equal("uptime", request.Command));
		Assert.All(_harness.Commands.Requests, request => Assert.Equal(256 * 1024, request.OutputLimitBytes));
		Assert.Equal(3, _harness.Sessions.Count);
		Assert.All(_harness.Sessions, session => Assert.True(session.IsDisposed));
		Assert.False(run.IsRunning);
	}

	[Fact]
	public async Task StartAsync_RefusesABlankCommand_AControlCharacter_AndNoLogins()
	{
		ConnectionProfile login = _harness.AddLogin("10.0.0.1");

		await Assert.ThrowsAsync<ArgumentException>(() => StartAsync("   ", [login]));
		await Assert.ThrowsAsync<ArgumentException>(() => StartAsync("echo \u0007", [login]));
		await Assert.ThrowsAsync<ArgumentException>(() => _harness.Runner.StartAsync(new CommandRunRequest { Command = "uptime", ConnectionIds = [] }, Ct));
		Assert.Equal(0, _harness.ShellProvider.ConnectCount);
	}

	[Fact]
	public async Task ARun_NeverHasMoreHostsGoingThanTheParallelLimit()
	{
		ConnectionProfile[] logins = [.. Enumerable.Range(1, 5).Select(number => _harness.AddLogin($"10.0.0.{number}"))];
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_harness.Commands.OnRun = async (_, cancellationToken) =>
		{
			await release.Task.WaitAsync(cancellationToken);
			return FakeCommandFeature.Outcome(0, "ok\n");
		};

		await using ICommandRun run = await StartAsync("uptime", logins, new CommandRunSettings { ParallelHosts = 2 });
		await Eventually.TrueAsync(() => _harness.Commands.Running == 2, "two hosts run");
		await Eventually.StaysTrueAsync(() => _harness.ShellProvider.ConnectCount == 2, "no third host connects while two are running");
		Assert.Equal(2, run.Hosts.Count(host => host.State == HostRunState.Running));
		Assert.Equal(3, run.Hosts.Count(host => host.State == HostRunState.Queued));

		release.SetResult();
		await run.WaitAsync(Ct);

		Assert.Equal(2, _harness.Commands.MostAtOnce);
		Assert.Equal(5, _harness.ShellProvider.ConnectCount);
		Assert.All(run.Hosts, host => Assert.Equal(HostRunState.Done, host.State));
	}

	[Fact]
	public async Task AHostThatRunsTooLong_FailsWithTheTimeout_AndItsConnectionCloses()
	{
		ConnectionProfile login = _harness.AddLogin("10.0.0.1");
		_harness.Commands.OnRun = async (_, cancellationToken) =>
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return FakeCommandFeature.Outcome(0, "");
		};

		await using ICommandRun run = await StartAsync("sleep 999", [login], new CommandRunSettings { TimeoutSeconds = 5 });
		await Eventually.TrueAsync(() => _harness.Commands.Running == 1, "the command runs");
		await Eventually.AdvanceUntilAsync(_harness.Time, TimeSpan.FromSeconds(1), () => run.Hosts[0].State == HostRunState.Failed, "the host times out");

		Assert.Equal("The command did not finish within 5 s.", run.Hosts[0].Message);
		Assert.True(Assert.Single(_harness.Sessions).IsDisposed);
	}

	[Fact]
	public async Task TimeOnAPrompt_DoesNotCountAgainstTheHost()
	{
		GatedInteraction prompts = new();
		_harness.Runner = _harness.CreateRunner(prompts);
		ConnectionProfile login = _harness.AddLogin("10.0.0.1", savedPassword: false);

		await using ICommandRun run = await StartAsync("uptime", [login], new CommandRunSettings { TimeoutSeconds = 5, SkipHostsThatAsk = false });
		await Eventually.TrueAsync(() => prompts.Asked, "the login prompt opens");
		_harness.Time.Advance(TimeSpan.FromMinutes(1));
		await Eventually.StaysTrueAsync(() => run.Hosts[0].State == HostRunState.Connecting, "the host waits on the user, not on the clock");

		prompts.Answer("ops", "pw");
		await run.WaitAsync(Ct);

		Assert.Equal(HostRunState.Done, run.Hosts[0].State);
	}

	[Fact]
	public async Task CancelAll_StopsRunningAndQueuedHosts_AndClosesTheirConnections()
	{
		ConnectionProfile[] logins = [_harness.AddLogin("10.0.0.1"), _harness.AddLogin("10.0.0.2"), _harness.AddLogin("10.0.0.3")];
		_harness.Commands.OnRun = async (_, cancellationToken) =>
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return FakeCommandFeature.Outcome(0, "");
		};

		await using ICommandRun run = await StartAsync("uptime", logins, new CommandRunSettings { ParallelHosts = 1 });
		await Eventually.TrueAsync(() => _harness.Commands.Running == 1, "the first host runs");
		run.CancelAll();
		await run.WaitAsync(Ct);

		Assert.Equal(HostRunState.Cancelled, run.Hosts[0].State);
		Assert.Equal("Cancelled.", run.Hosts[0].Message);
		Assert.All(run.Hosts.Skip(1), host => Assert.Equal("Cancelled before it started.", host.Message));
		Assert.Equal(1, _harness.ShellProvider.ConnectCount);
		Assert.True(Assert.Single(_harness.Sessions).IsDisposed);
		Assert.False(run.IsRunning);
	}

	[Fact]
	public async Task SkippingHostsThatAsk_SkipsALoginWithNoSavedPassword_WithoutAsking()
	{
		ConnectionProfile login = _harness.AddLogin("10.0.0.1", savedPassword: false);

		await using ICommandRun run = await StartAsync("uptime", [login]);
		await run.WaitAsync(Ct);

		CommandHostResult host = Assert.Single(run.Hosts);
		Assert.Equal(HostRunState.Skipped, host.State);
		Assert.Equal("It would ask for the login.", host.Message);
		Assert.Empty(_harness.Interaction.CredentialPrompts);
		Assert.Empty(_harness.Sessions);
	}

	[Fact]
	public async Task SkippingHostsThatAsk_SkipsAnUntrustedHostKey_BeforeTheVerifierCanAsk()
	{
		_harness.KnownHosts.Match = HostIdentityMatch.Unknown;
		ConnectionProfile login = _harness.AddLogin("10.0.0.1");

		await using ICommandRun run = await StartAsync("uptime", [login]);
		await run.WaitAsync(Ct);

		CommandHostResult host = Assert.Single(run.Hosts);
		Assert.Equal(HostRunState.Skipped, host.State);
		Assert.Equal("It would ask about its host key, which is not trusted yet.", host.Message);
		Assert.Equal(0, _harness.Verifier.Calls);
	}

	[Theory]
	[InlineData(HostKeyPolicy.AcceptNew, HostRunState.Done)]
	[InlineData(HostKeyPolicy.Strict, HostRunState.Failed)]
	public async Task SkippingHostsThatAsk_LeavesAPolicyThatDoesNotAskToTheVerifier(HostKeyPolicy policy, HostRunState expected)
	{
		_harness.KnownHosts.Match = HostIdentityMatch.Unknown;
		_harness.Settings.Set(new SecuritySettings { HostKeyPolicy = policy });
		_harness.Verifier.Answer = policy == HostKeyPolicy.AcceptNew;
		ConnectionProfile login = _harness.AddLogin("10.0.0.1");

		await using ICommandRun run = await StartAsync("uptime", [login]);
		await run.WaitAsync(Ct);

		Assert.Equal(expected, Assert.Single(run.Hosts).State);
		Assert.Equal(1, _harness.Verifier.Calls);
	}

	[Fact]
	public async Task WithAskingAllowed_ThePromptReachesTheWindow()
	{
		_harness.Interaction.AnswerCredentials("ops", "pw");
		ConnectionProfile login = _harness.AddLogin("10.0.0.1", savedPassword: false);

		await using ICommandRun run = await StartAsync("uptime", [login], new CommandRunSettings { SkipHostsThatAsk = false });
		await run.WaitAsync(Ct);

		Assert.Equal(HostRunState.Done, Assert.Single(run.Hosts).State);
		_ = Assert.Single(_harness.Interaction.CredentialPrompts);
	}

	[Fact]
	public async Task RetrySkippedAsync_RunsTheSkippedHostsAgain_AskingThisTime()
	{
		ConnectionProfile asks = _harness.AddLogin("10.0.0.1", savedPassword: false);
		ConnectionProfile saved = _harness.AddLogin("10.0.0.2");
		await using ICommandRun run = await StartAsync("uptime", [asks, saved]);
		await run.WaitAsync(Ct);
		Assert.Equal(HostRunState.Skipped, run.Hosts[0].State);

		_harness.Interaction.AnswerCredentials("ops", "pw");
		Assert.Equal(1, await run.RetrySkippedAsync(Ct));
		await run.WaitAsync(Ct);

		Assert.Equal(HostRunState.Done, run.Hosts[0].State);
		Assert.Equal(2, run.Hosts[0].Attempt);
		Assert.Equal(1, run.Hosts[1].Attempt);
		_ = Assert.Single(_harness.Interaction.CredentialPrompts);
	}

	[Fact]
	public async Task RetryFailedAsync_RunsOnlyTheFailedHostsAgain()
	{
		ConnectionProfile flaky = _harness.AddLogin("10.0.0.1");
		ConnectionProfile steady = _harness.AddLogin("10.0.0.2");
		int flakyAttempts = 0;
		_harness.ShellProvider.OnConnect = (context, cancellationToken) =>
			context.Host.Address == "10.0.0.1" && Interlocked.Increment(ref flakyAttempts) == 1
				? Task.FromException<IProtocolSession>(new ProtocolConnectException(ConnectFailure.HostUnreachable, "No route to 10.0.0.1."))
				: _harness.ConnectAsync(context, cancellationToken);

		await using ICommandRun run = await StartAsync("uptime", [flaky, steady]);
		await run.WaitAsync(Ct);
		Assert.Equal(HostRunState.Failed, run.Hosts[0].State);
		Assert.Equal("No route to 10.0.0.1.", run.Hosts[0].Message);
		Assert.True(run.Hosts[0].IsProblem);

		Assert.Equal(1, await run.RetryFailedAsync(Ct));
		await run.WaitAsync(Ct);

		Assert.Equal(HostRunState.Done, run.Hosts[0].State);
		Assert.Null(run.Hosts[0].Message);
		Assert.Equal(2, run.Hosts[0].Attempt);
		Assert.Equal(1, run.Hosts[1].Attempt);
		Assert.Equal(3, _harness.ShellProvider.ConnectCount);
		Assert.Equal(0, await run.RetryFailedAsync(Ct));
	}

	[Fact]
	public async Task ANonZeroExit_IsDoneButCountsAsAProblem()
	{
		_harness.Commands.OnRun = (_, _) => Task.FromResult(FakeCommandFeature.Outcome(3, "", "no such unit\n"));
		ConnectionProfile login = _harness.AddLogin("10.0.0.1");

		await using ICommandRun run = await StartAsync("systemctl status nope", [login]);
		await run.WaitAsync(Ct);

		CommandHostResult host = Assert.Single(run.Hosts);
		Assert.Equal(HostRunState.Done, host.State);
		Assert.Equal(3, host.ExitCode);
		Assert.Equal("no such unit\n", host.Error.Text);
		Assert.True(host.IsProblem);
	}

	[Fact]
	public async Task TheOutputCap_ReachesTheFeature_AndTheRowSaysAStreamWasCut()
	{
		string escape = ((char)0x1b).ToString();
		byte[] kept = Encoding.UTF8.GetBytes(new string('a', 4096));
		_harness.Commands.OnRun = (_, _) => Task.FromResult(new RemoteCommandOutcome
		{
			ExitCode = 0,
			Output = new CapturedOutput(kept, 1_000_000),
			Error = FakeCommandFeature.Captured($"{escape}[31merror{escape}[0m\r\nsecond\rthird"),
		});
		ConnectionProfile login = _harness.AddLogin("10.0.0.1");

		await using ICommandRun run = await StartAsync("cat big.log", [login], new CommandRunSettings { OutputKilobytes = 4 });
		await run.WaitAsync(Ct);

		Assert.Equal(4096, Assert.Single(_harness.Commands.Requests).OutputLimitBytes);
		CommandHostResult host = Assert.Single(run.Hosts);
		Assert.True(host.Output.IsTruncated);
		Assert.Equal(1_000_000, host.Output.TotalBytes);
		Assert.Equal(4096, host.Output.Text.Length);
		Assert.Equal("error\nsecond\nthird", host.Error.Text);
		Assert.False(host.Error.IsTruncated);
	}

	[Fact]
	public async Task ALoginThatCannotRunCommands_FailsWithTheReason_AndNeverConnects()
	{
		ConnectionProfile files = _harness.AddLogin("10.0.0.1", protocolId: TestProtocols.Sftp.Id);
		Guid deleted = Guid.NewGuid();

		await using ICommandRun run = await _harness.Runner.StartAsync(new CommandRunRequest { Command = "uptime", ConnectionIds = [files.Id, deleted] }, Ct);
		await run.WaitAsync(Ct);

		Assert.Equal(HostRunState.Failed, run.Hosts[0].State);
		Assert.Equal(CommandRunEligibility.WhyNot(TestProtocols.Sftp), run.Hosts[0].Message);
		Assert.Equal(HostRunState.Failed, run.Hosts[1].State);
		Assert.Equal("This login no longer exists.", run.Hosts[1].Message);
		Assert.Equal("Deleted login", run.Hosts[1].Title);
		Assert.Equal(0, _harness.SftpProvider.ConnectCount);
		Assert.Equal(0, _harness.ShellProvider.ConnectCount);
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
		ConnectionProfile login = _harness.AddLogin("10.0.0.1");
		if (ending == Ending.NoFeature)
		{
			_harness.ShellProvider.OnConnect = (_, _) => Task.FromResult<IProtocolSession>(_harness.Track(new FakeProtocolSession()));
		}

		_harness.Commands.OnRun = ending switch
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
		await Eventually.TrueAsync(() => _harness.Sessions.Count == 1, "the host connects");
		switch (ending)
		{
			case Ending.Cancelled:
				await Eventually.TrueAsync(() => _harness.Commands.Running == 1, "the command runs");
				run.CancelAll();
				await run.WaitAsync(Ct);
				break;
			case Ending.RunDisposed:
				await Eventually.TrueAsync(() => _harness.Commands.Running == 1, "the command runs");
				await run.DisposeAsync();
				break;
			case Ending.RunnerDisposed:
				await Eventually.TrueAsync(() => _harness.Commands.Running == 1, "the command runs");
				await _harness.Runner.DisposeAsync();
				break;
			default:
				await run.WaitAsync(Ct);
				break;
		}

		await Eventually.TrueAsync(() => _harness.Sessions[0].IsDisposed, "the connection is closed");
		CommandHostResult host = Assert.Single(run.Hosts);
		Assert.Equal(
			ending switch
			{
				Ending.Done => HostRunState.Done,
				Ending.CommandFailed or Ending.NoFeature => HostRunState.Failed,
				_ => HostRunState.Cancelled,
			},
			host.State);
		await run.DisposeAsync();
	}

	[Fact]
	public async Task DisposingTheRunner_RefusesNewRuns()
	{
		ConnectionProfile login = _harness.AddLogin("10.0.0.1");
		await _harness.Runner.DisposeAsync();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => StartAsync("uptime", [login]));
	}

	[Fact]
	public async Task NothingTheCommandSaysReachesTheLog()
	{
		const string Token = "tok3n-s3cr3t";
		_harness.Commands.OnRun = (_, _) => Task.FromException<RemoteCommandOutcome>(new InvalidOperationException($"curl -H {Token} broke"));
		ConnectionProfile login = _harness.AddLogin("10.0.0.1");

		await using ICommandRun run = await StartAsync($"curl -H 'Authorization: {Token}' https://example.test", [login]);
		await run.WaitAsync(Ct);

		CommandHostResult host = Assert.Single(run.Hosts);
		Assert.Equal(HostRunState.Failed, host.State);
		Assert.DoesNotContain(Token, host.Message, StringComparison.Ordinal);
		Assert.NotEmpty(_harness.Logger.Entries);
		Assert.All(_harness.Logger.Entries, entry => Assert.DoesNotContain(Token, entry.Message, StringComparison.Ordinal));
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

	private Task<ICommandRun> StartAsync(string command, IEnumerable<ConnectionProfile> logins, CommandRunSettings? options = null) =>
		_harness.Runner.StartAsync(
			new CommandRunRequest
			{
				Command = command,
				ConnectionIds = [.. logins.Select(login => login.Id)],
				Options = options ?? new CommandRunSettings(),
			},
			Ct);
}
