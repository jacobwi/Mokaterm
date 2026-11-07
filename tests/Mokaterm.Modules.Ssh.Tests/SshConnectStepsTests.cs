using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Protocols;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// The chain of steps an SSH or SFTP connect reports for the session tab, and where it stands when the connect stops
/// before a real server would be needed: nothing listening, a server that greets and goes quiet, an unreachable jump
/// host and a login that cannot even be made.
/// </summary>
public sealed class SshConnectStepsTests
{
	private static readonly string[] ShellSteps = [SshConnectSteps.Connect, SshConnectSteps.HostKey, SshConnectSteps.Login, SshConnectSteps.Open];

	[Fact]
	public void Plan_FollowsTheWire_AndEndsWithWhatTheSessionOpens()
	{
		IReadOnlyList<ConnectStep> shell = SshConnectSteps.For(new SshConnectionOptions(), "Shell");
		Assert.Equal(ShellSteps, shell.Select(step => step.Id));
		Assert.Equal("Shell", shell[^1].Label);
		Assert.Equal(ConnectStepKind.Session, shell[^1].Kind);

		Assert.Equal(ShellSteps.SkipLast(1), SshConnectSteps.For(new SshConnectionOptions(), opens: null).Select(step => step.Id));

		IReadOnlyList<ConnectStep> oneHop = SshConnectSteps.For(new SshConnectionOptions { JumpConnectionIds = [Guid.NewGuid()] }, "Shell");
		Assert.Equal([SshConnectSteps.JumpHosts, .. ShellSteps], oneHop.Select(step => step.Id));
		Assert.Equal("Jump host", oneHop[0].Label);
		Assert.Equal("Jump hosts", SshConnectSteps.For(new SshConnectionOptions { JumpConnectionIds = [Guid.NewGuid(), Guid.NewGuid()] }, "Shell")[0].Label);
	}

	[Fact]
	public async Task Connect_NothingListening_StopsAtTheConnect()
	{
		RecordingConnectSteps steps = new();

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => Ssh().ConnectAsync(Context(LoopbackPort.Free(), steps), TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal(ShellSteps, steps.Ids);
		Assert.Equal(SshConnectSteps.Connect, steps.Chain.Current?.Step.Id);
		Assert.Equal(SshConnectSteps.Connect, steps.EndedWith(failure.Message).Failed?.Step.Id);
	}

	[Fact]
	public async Task Connect_AServerThatGreetsAndGoesQuiet_StopsAtTheHostKey()
	{
		using GreetingServer server = new("SSH-2.0-OpenSSH_9.6\r\n");
		RecordingConnectSteps steps = new();
		FakeSettingsService settings = new();
		settings.Set(new SshSettings { ConnectTimeoutSeconds = 1 });

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => Ssh(settings).ConnectAsync(Context(server.Port, steps), TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.Timeout, failure.Failure);
		Assert.Equal(ConnectStepState.Done, steps.StateOf(SshConnectSteps.Connect));
		Assert.Equal(SshConnectSteps.HostKey, steps.Chain.Current?.Step.Id);
	}

	[Fact]
	public async Task Connect_AJumpHostNobodyAnswers_StopsAtTheJumpHost()
	{
		FakeConnectionResolver resolver = new();
		Guid hop = resolver.Add("127.0.0.1", LoopbackPort.Free());
		RecordingConnectSteps steps = new();
		ProtocolOptions options = ProtocolOptions.Empty.With(SshConnectionOptions.JumpHostsKey, hop.ToString("D"));

		await Assert.ThrowsAsync<ProtocolConnectException>(
			() => Ssh().ConnectAsync(Context(22, steps, options, resolver: resolver), TestContext.Current.CancellationToken));

		// The hop's own dial is the jump host's step, not the target's.
		Assert.Equal([SshConnectSteps.JumpHosts, .. ShellSteps], steps.Ids);
		Assert.Equal(SshConnectSteps.JumpHosts, steps.Chain.Current?.Step.Id);
		Assert.Equal(ConnectStepState.Pending, steps.StateOf(SshConnectSteps.Connect));
	}

	[Fact]
	public async Task Connect_ALoginThatCannotBeMade_FailsTheLoginBeforeDialing()
	{
		using GreetingServer server = new("SSH-2.0-OpenSSH_9.6\r\n");
		RecordingConnectSteps steps = new();

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => Ssh().ConnectAsync(Context(server.Port, steps, credentials: new KeylessCredentialSource()), TestContext.Current.CancellationToken));

		Assert.Equal(ConnectFailure.AuthenticationFailed, failure.Failure);
		Assert.Equal(0, server.Accepted);
		Assert.Equal(SshConnectSteps.Login, steps.Chain.Failed?.Step.Id);
		Assert.Equal(failure.Message, steps.Chain.Failed?.Detail);
		Assert.Equal(ConnectStepState.Pending, steps.StateOf(SshConnectSteps.Connect));
		Assert.Equal(ConnectStepState.Pending, steps.StateOf(SshConnectSteps.HostKey));
	}

	[Fact]
	public async Task Sftp_PlansTheFileChannelLast()
	{
		RecordingConnectSteps steps = new();
		SftpProtocolProvider sftp = new(new SshConnector(new FakeSettingsService()));

		await Assert.ThrowsAsync<ProtocolConnectException>(
			() => sftp.ConnectAsync(Context(LoopbackPort.Free(), steps), TestContext.Current.CancellationToken));

		Assert.Equal(ShellSteps, steps.Ids);
		Assert.Equal("SFTP", steps.Chain.Steps[^1].Step.Label);
	}

	[Fact]
	public async Task Commands_PlanNothingAfterTheLogin()
	{
		RecordingConnectSteps steps = new();

		await Assert.ThrowsAsync<ProtocolConnectException>(
			() => Ssh().ConnectAsync(Context(LoopbackPort.Free(), steps, purpose: ConnectPurpose.Commands), TestContext.Current.CancellationToken));

		Assert.Equal(ShellSteps.SkipLast(1), steps.Ids);
	}

	private static SshProtocolProvider Ssh(FakeSettingsService? settings = null) =>
		new(new SshConnector(settings ?? new FakeSettingsService()));

	private static ProtocolConnectContext Context(
		int port,
		RecordingConnectSteps steps,
		ProtocolOptions? options = null,
		ICredentialSource? credentials = null,
		IConnectionResolver? resolver = null,
		ConnectPurpose purpose = ConnectPurpose.Interactive)
	{
		HostProfile host = new() { Id = Guid.NewGuid(), Address = "127.0.0.1" };
		ICredentialSource source = credentials ?? new PasswordCredentialSource("tester", "secret");
		return new ProtocolConnectContext
		{
			SessionId = Guid.NewGuid(),
			Host = host,
			Connection = new ConnectionProfile
			{
				Id = Guid.NewGuid(),
				HostId = host.Id,
				ProtocolId = SshProtocolIds.Ssh,
				Port = port,
				Username = source.Username,
				AuthenticationMethod = source.Method,
				Options = options ?? ProtocolOptions.Empty,
			},
			Port = port,
			Credentials = source,
			HostVerifier = FakeHostVerifier.Accepting(),
			Interaction = new DismissingInteraction(),
			Resolver = resolver,
			Steps = steps,
			Purpose = purpose,
		};
	}

	/// <summary>A key login whose key was never saved.</summary>
	private sealed class KeylessCredentialSource : ICredentialSource
	{
		public string? Username => "tester";

		public AuthenticationMethod Method => AuthenticationMethod.PublicKey;

		public ValueTask<LoginCredentials?> GetAsync(CancellationToken cancellationToken) =>
			ValueTask.FromResult<LoginCredentials?>(new LoginCredentials { Username = "tester", Method = AuthenticationMethod.PublicKey });

		public ValueTask<LoginCredentials?> RetryAsync(string reason, CancellationToken cancellationToken) =>
			ValueTask.FromResult<LoginCredentials?>(null);
	}
}
