using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Fleet;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Sessions.Tests.Fakes;

namespace Mokaterm.Sessions.Tests;

/// <summary>A survey of the machines through the pipeline it shares with command runs, and what its rows keep.</summary>
public sealed class HostSurveyTests : IAsyncDisposable
{
	private readonly HostRunHarness _harness = new();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	public ValueTask DisposeAsync() => _harness.DisposeAsync();

	[Fact]
	public async Task StartAsync_ReadsEveryLogin_OverConnectionsOpenedOnlyForCommands()
	{
		ConnectionProfile[] logins = [_harness.AddLogin("10.0.0.1"), _harness.AddLogin("10.0.0.2")];

		await using IHostSurvey survey = await StartAsync(logins);
		await survey.WaitAsync(Ct);

		Assert.Equal(["ops@10.0.0.1", "ops@10.0.0.2"], survey.Hosts.Select(host => host.Title));
		Assert.All(survey.Hosts, host =>
		{
			Assert.Equal(HostRunState.Done, host.State);
			Assert.Equal("Ubuntu 24.04.1 LTS", host.Summary?.OperatingSystem);
			Assert.Equal(_harness.Time.GetUtcNow(), host.ReadAt);
			Assert.Null(host.Message);
			Assert.False(host.IsProblem);
		});

		Assert.All(_harness.ShellProvider.Contexts, context => Assert.Equal(ConnectPurpose.Commands, context.Purpose));
		Assert.Empty(_harness.Commands.Requests);
		Assert.Equal(2, _harness.Summaries.Reads);
		Assert.All(_harness.Sessions, session => Assert.True(session.IsDisposed));
	}

	[Fact]
	public async Task AReadThatFailsLater_KeepsTheLastSummary_AndItsTime()
	{
		ConnectionProfile login = _harness.AddLogin("10.0.0.1");
		await using IHostSurvey survey = await StartAsync([login]);
		await survey.WaitAsync(Ct);
		DateTimeOffset? firstRead = survey.Hosts[0].ReadAt;

		_harness.Time.Advance(TimeSpan.FromMinutes(5));
		_harness.Summaries.OnRead = _ => Task.FromException<HostSummary>(new RemoteCommandException("The SSH connection is closed."));
		Assert.Equal(1, await survey.RefreshAsync([login.Id], cancellationToken: Ct));
		await survey.WaitAsync(Ct);

		HostSurveyResult host = Assert.Single(survey.Hosts);
		Assert.Equal(HostRunState.Failed, host.State);
		Assert.Equal("The SSH connection is closed.", host.Message);
		Assert.Equal("Ubuntu 24.04.1 LTS", host.Summary?.OperatingSystem);
		Assert.Equal(firstRead, host.ReadAt);
		Assert.Equal(2, host.Attempt);
	}

	[Fact]
	public async Task RefreshAsync_AddsLoginsTheSurveyDidNotHave_AndLeavesOnesStillBeingReadAlone()
	{
		ConnectionProfile first = _harness.AddLogin("10.0.0.1");
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_harness.Summaries.OnRead = async cancellationToken =>
		{
			await release.Task.WaitAsync(cancellationToken);
			return new HostSummary { OperatingSystem = "Debian GNU/Linux 12 (bookworm)" };
		};

		await using IHostSurvey survey = await StartAsync([first]);
		await Eventually.TrueAsync(() => _harness.Summaries.Running == 1, "the first machine is being read");

		ConnectionProfile second = _harness.AddLogin("10.0.0.2");
		Assert.Equal(1, await survey.RefreshAsync([first.Id, second.Id], cancellationToken: Ct));
		release.SetResult();
		await survey.WaitAsync(Ct);

		Assert.Equal([first.Id, second.Id], survey.Hosts.Select(host => host.ConnectionId));
		Assert.All(survey.Hosts, host => Assert.Equal(HostRunState.Done, host.State));
		Assert.Equal(1, survey.Hosts[0].Attempt);
		Assert.Equal(2, _harness.Summaries.Reads);
	}

	[Fact]
	public async Task ALoginThatWouldAsk_IsSkipped_ThenReadAskingWhenAsked()
	{
		ConnectionProfile asks = _harness.AddLogin("10.0.0.1", savedPassword: false);
		await using IHostSurvey survey = await StartAsync([asks]);
		await survey.WaitAsync(Ct);

		Assert.Equal(HostRunState.Skipped, survey.Hosts[0].State);
		Assert.Equal("It would ask for the login.", survey.Hosts[0].Message);
		Assert.Null(survey.Hosts[0].Summary);
		Assert.Empty(_harness.Interaction.CredentialPrompts);

		_harness.Interaction.AnswerCredentials("ops", "pw");
		Assert.Equal(1, await survey.RetrySkippedAsync(Ct));
		await survey.WaitAsync(Ct);

		Assert.Equal(HostRunState.Done, survey.Hosts[0].State);
		_ = Assert.Single(_harness.Interaction.CredentialPrompts);
	}

	[Fact]
	public async Task RefreshAsync_Asking_ReachesTheWindow()
	{
		ConnectionProfile asks = _harness.AddLogin("10.0.0.1", savedPassword: false);
		await using IHostSurvey survey = await StartAsync([asks]);
		await survey.WaitAsync(Ct);

		_harness.Interaction.AnswerCredentials("ops", "pw");
		Assert.Equal(1, await survey.RefreshAsync([asks.Id], askPrompts: true, Ct));
		await survey.WaitAsync(Ct);

		Assert.Equal(HostRunState.Done, survey.Hosts[0].State);
	}

	[Fact]
	public async Task ALoginWhoseProtocolCannotReport_FailsWithTheReason_AndNeverConnects()
	{
		ConnectionProfile commandsOnly = _harness.AddLogin("10.0.0.1", protocolId: HostRunHarness.CommandsOnly.Id);

		await using IHostSurvey survey = await StartAsync([commandsOnly]);
		await survey.WaitAsync(Ct);

		Assert.Equal(HostRunState.Failed, survey.Hosts[0].State);
		Assert.Equal("Commands only logins cannot report on their machine.", survey.Hosts[0].Message);
		Assert.Equal(HostSurveyEligibility.WhyNot(HostRunHarness.CommandsOnly), survey.Hosts[0].Message);
		Assert.Equal(0, _harness.CommandsOnlyProvider.ConnectCount);
	}

	[Fact]
	public async Task ASessionWithoutTheFeature_Fails_AndItsConnectionCloses()
	{
		ConnectionProfile login = _harness.AddLogin("10.0.0.1");
		_harness.ShellProvider.OnConnect = (_, _) => Task.FromResult<IProtocolSession>(_harness.Track(new FakeProtocolSession { Features = [_harness.Commands] }));

		await using IHostSurvey survey = await StartAsync([login]);
		await survey.WaitAsync(Ct);

		Assert.Equal(HostRunState.Failed, survey.Hosts[0].State);
		Assert.Equal("This connection cannot report on its machine.", survey.Hosts[0].Message);
		Assert.True(Assert.Single(_harness.Sessions).IsDisposed);
	}

	[Fact]
	public async Task AReadThatOutlastsItsTime_FailsInTheSurveysOwnWords()
	{
		ConnectionProfile login = _harness.AddLogin("10.0.0.1");
		_harness.Summaries.OnRead = async cancellationToken =>
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return new HostSummary();
		};

		await using IHostSurvey survey = await StartAsync([login], new CommandRunSettings { TimeoutSeconds = 5 });
		await Eventually.TrueAsync(() => _harness.Summaries.Running == 1, "the machine is being read");
		await Eventually.AdvanceUntilAsync(_harness.Time, TimeSpan.FromSeconds(1), () => survey.Hosts[0].State == HostRunState.Failed, "the read times out");

		Assert.Equal("Reading the machine did not finish within 5 s.", survey.Hosts[0].Message);
		Assert.True(Assert.Single(_harness.Sessions).IsDisposed);
	}

	[Fact]
	public async Task OneRunnerServesBothKinds_AndDisposingItEndsBoth()
	{
		ConnectionProfile login = _harness.AddLogin("10.0.0.1");
		_harness.Commands.OnRun = async (_, cancellationToken) =>
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return FakeCommandFeature.Outcome(0, "");
		};

		_harness.Summaries.OnRead = async cancellationToken =>
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return new HostSummary();
		};

		ICommandRun run = await _harness.Runner.StartAsync(new CommandRunRequest { Command = "uptime", ConnectionIds = [login.Id] }, Ct);
		IHostSurvey survey = await StartAsync([login]);
		await Eventually.TrueAsync(() => _harness.Commands.Running == 1 && _harness.Summaries.Running == 1, "both are under way");

		await _harness.Runner.DisposeAsync();

		Assert.Equal(HostRunState.Cancelled, run.Hosts[0].State);
		Assert.Equal(HostRunState.Cancelled, survey.Hosts[0].State);
		Assert.Equal(2, _harness.Sessions.Count);
		Assert.All(_harness.Sessions, session => Assert.True(session.IsDisposed));
		await Assert.ThrowsAsync<ObjectDisposedException>(() => StartAsync([login]));
	}

	[Fact]
	public async Task StartAsync_RefusesNoLogins()
	{
		await Assert.ThrowsAsync<ArgumentException>(() => _harness.Runner.StartAsync(new HostSurveyRequest { ConnectionIds = [] }, Ct));
		Assert.Equal(0, _harness.ShellProvider.ConnectCount);
	}

	private Task<IHostSurvey> StartAsync(IEnumerable<ConnectionProfile> logins, CommandRunSettings? options = null) =>
		_harness.Runner.StartAsync(
			new HostSurveyRequest
			{
				ConnectionIds = [.. logins.Select(login => login.Id)],
				Options = options ?? new CommandRunSettings(),
			},
			Ct);
}
