using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Sessions.Protocols;
using Mokaterm.Sessions.Tests.Fakes;

namespace Mokaterm.Sessions.Tests;

/// <summary>
/// The connect steps a provider reports, as the session tab reads them from its handle: one chain per attempt, kept
/// where the attempt failed, and never redrawn by an attempt that is no longer the current one.
/// </summary>
public sealed class SessionConnectStepsTests : IAsyncDisposable
{
	private static readonly HostProfile Host = new() { Id = Guid.NewGuid(), Address = "10.0.0.5" };

	private static readonly ConnectStep[] Plan =
	[
		new("dial", "Connect", ConnectStepKind.Dial),
		new("key", "Host key", ConnectStepKind.HostIdentity),
		new("login", "Login", ConnectStepKind.Login),
	];

	private readonly FakeConnectionRepository _repository = new();
	private readonly FakeCredentialStore _credentials = new();
	private readonly FakeProtocolProvider _ssh = new(TestProtocols.Ssh);
	private readonly ConnectionProfile _connection;
	private readonly SessionManager _manager;

	public SessionConnectStepsTests()
	{
		CredentialInfo password = _credentials.Add(
			new CredentialInfo { Id = Guid.NewGuid(), Name = "shared", Kind = CredentialKind.Password, IsShared = true },
			new CredentialSecretInput { Password = "pw" });
		_connection = new ConnectionProfile
		{
			Id = Guid.NewGuid(),
			HostId = Host.Id,
			ProtocolId = "ssh",
			Username = "abc",
			AuthenticationMethod = AuthenticationMethod.Password,
			CredentialId = password.Id,
		};

		_repository.Add(Host, _connection);
		_manager = new SessionManager(
			_repository,
			_credentials,
			new ProtocolRegistry([_ssh]),
			new FakeHostIdentityVerifier(),
			new FakeUserInteraction(),
			new UncachedSettingsService(),
			new ListLogger<SessionManager>(),
			new FakeTimeProvider(new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero)));
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Connecting_ShowsTheStepsAsTheProviderReportsThem_AndSaysSo()
	{
		TaskCompletionSource<IProtocolSession> connect = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_ssh.OnConnect = (context, _) =>
		{
			context.Steps?.Plan(Plan);
			context.Steps?.Start("key");
			return connect.Task;
		};

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		int changes = 0;
		handle.Changed += () => Interlocked.Increment(ref changes);
		await Eventually.TrueAsync(() => handle.ConnectSteps.Current?.Step.Id == "key", "the step under way reaches the handle");

		Assert.Equal(SessionState.Connecting, handle.State);
		Assert.Equal([ConnectStepState.Done, ConnectStepState.Running, ConnectStepState.Pending], States(handle));

		int before = Volatile.Read(ref changes);
		ProtocolConnectContext context = Assert.Single(_ssh.Contexts);
		context.Steps?.Start("login", "publickey");
		Assert.True(Volatile.Read(ref changes) > before);
		Assert.Equal("publickey", handle.ConnectSteps.Current?.Detail);

		// A report that changes nothing raises nothing.
		before = Volatile.Read(ref changes);
		context.Steps?.Start("login", "publickey");
		Assert.Equal(before, Volatile.Read(ref changes));
		connect.SetResult(_ssh.CreateSession());
	}

	[Fact]
	public async Task AFailure_LandsOnTheStepUnderWay_WithItsMessage()
	{
		_ssh.OnConnect = (context, _) =>
		{
			context.Steps?.Plan(Plan);
			context.Steps?.Start("key");
			return Task.FromException<IProtocolSession>(new ProtocolConnectException(ConnectFailure.Timeout, "Timed out connecting to 10.0.0.5:22."));
		};

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		await Eventually.TrueAsync(() => handle.State == SessionState.Failed, "the session fails");

		ConnectStepStatus failed = Assert.IsType<ConnectStepStatus>(handle.ConnectSteps.Failed);
		Assert.Equal("key", failed.Step.Id);
		Assert.Equal("Timed out connecting to 10.0.0.5:22.", failed.Detail);
		Assert.Equal([ConnectStepState.Done, ConnectStepState.Failed, ConnectStepState.Pending], States(handle));
	}

	[Fact]
	public async Task AFailureTheProviderPlacedItself_IsKept()
	{
		_ssh.OnConnect = (context, _) =>
		{
			context.Steps?.Plan(Plan);
			context.Steps?.Fail("login", "No private key is saved for this login.");
			return Task.FromException<IProtocolSession>(new ProtocolConnectException(ConnectFailure.AuthenticationFailed, "No private key is saved for this login."));
		};

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		await Eventually.TrueAsync(() => handle.State == SessionState.Failed, "the session fails");

		Assert.Equal([ConnectStepState.Pending, ConnectStepState.Pending, ConnectStepState.Failed], States(handle));
	}

	[Fact]
	public async Task Connecting_ClosesEveryStep_EvenOnesNeverReported()
	{
		_ssh.OnConnect = (context, _) =>
		{
			context.Steps?.Plan(Plan);
			context.Steps?.Start("dial");
			return Task.FromResult<IProtocolSession>(_ssh.CreateSession());
		};

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		await Eventually.TrueAsync(() => handle.State == SessionState.Connected, "the session connects");

		Assert.All(handle.ConnectSteps.Steps, status => Assert.Equal(ConnectStepState.Done, status.State));
	}

	[Fact]
	public async Task AProviderThatReportsNoSteps_LeavesTheChainEmpty()
	{
		_ssh.OnConnect = (_, _) => Task.FromException<IProtocolSession>(new ProtocolConnectException(ConnectFailure.HostUnreachable, "No route to host."));

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		await Eventually.TrueAsync(() => handle.State == SessionState.Failed, "the session fails");

		Assert.True(handle.ConnectSteps.IsEmpty);
		Assert.Equal("No route to host.", handle.StatusMessage);
	}

	[Fact]
	public async Task AReconnect_StartsAFreshChain_AndTheOldAttemptCannotTouchIt()
	{
		TaskCompletionSource<IProtocolSession> second = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_ssh.OnConnect = (context, _) =>
		{
			context.Steps?.Plan(Plan);
			context.Steps?.Start("key");
			return Task.FromException<IProtocolSession>(new ProtocolConnectException(ConnectFailure.Timeout, "Timed out."));
		};

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		await Eventually.TrueAsync(() => handle.State == SessionState.Failed, "the first attempt fails");
		IConnectSteps first = Assert.IsAssignableFrom<IConnectSteps>(Assert.Single(_ssh.Contexts).Steps);

		_ssh.OnConnect = (_, _) => second.Task;
		await _manager.ReconnectAsync(handle.Id, Ct);
		await Eventually.TrueAsync(() => _ssh.ConnectCount == 2 && handle.State == SessionState.Connecting, "the second attempt is connecting");

		Assert.True(handle.ConnectSteps.IsEmpty);
		IConnectSteps current = Assert.IsAssignableFrom<IConnectSteps>(_ssh.Contexts[1].Steps);
		Assert.NotSame(first, current);

		// The first attempt's provider wakes up late and reports; the second attempt's chain is what the tab shows.
		first.Plan(Plan);
		first.Start("login");
		Assert.True(handle.ConnectSteps.IsEmpty);

		current.Plan(Plan);
		current.Start("dial");
		Assert.Equal("dial", handle.ConnectSteps.Current?.Step.Id);
		second.SetResult(_ssh.CreateSession());
	}

	[Fact]
	public async Task AFailedAttempt_KeepsItsChain_AgainstLateReports()
	{
		_ssh.OnConnect = (context, _) =>
		{
			context.Steps?.Plan(Plan);
			context.Steps?.Start("dial");
			return Task.FromException<IProtocolSession>(new ProtocolConnectException(ConnectFailure.HostUnreachable, "Connection refused."));
		};

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		await Eventually.TrueAsync(() => handle.State == SessionState.Failed, "the session fails");
		ConnectStepChain failed = handle.ConnectSteps;

		IConnectSteps late = Assert.IsAssignableFrom<IConnectSteps>(Assert.Single(_ssh.Contexts).Steps);
		late.Start("login");
		late.Plan(Plan);

		Assert.Same(failed, handle.ConnectSteps);
		Assert.Equal("dial", handle.ConnectSteps.Failed?.Step.Id);
	}

	[Fact]
	public async Task ADisconnectWhileConnecting_StopsTheChainWhereItWas()
	{
		_ssh.OnConnect = async (context, cancellationToken) =>
		{
			context.Steps?.Plan(Plan);
			context.Steps?.Start("key");
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return new FakeProtocolSession();
		};

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		await Eventually.TrueAsync(() => handle.ConnectSteps.Current is not null, "the provider is on a step");

		await _manager.DisconnectAsync(handle.Id);
		Assert.IsAssignableFrom<IConnectSteps>(Assert.Single(_ssh.Contexts).Steps).Start("login");

		Assert.Equal(SessionState.Disconnected, handle.State);
		Assert.Equal("key", handle.ConnectSteps.Current?.Step.Id);
		Assert.Null(handle.ConnectSteps.Failed);
	}

	[Fact]
	public async Task ABrokenPlan_ThrowsBackAtTheProvider()
	{
		_ssh.OnConnect = (context, _) =>
		{
			context.Steps?.Plan([new ConnectStep("dial", "Connect"), new ConnectStep("dial", "Again")]);
			return Task.FromResult<IProtocolSession>(_ssh.CreateSession());
		};

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		await Eventually.TrueAsync(() => handle.State == SessionState.Failed, "the provider's own exception fails the connect");

		Assert.True(handle.ConnectSteps.IsEmpty);
		Assert.Equal(ConnectFailure.Unknown, handle.Failure);
	}

	public ValueTask DisposeAsync() => _manager.DisposeAsync();

	private static ConnectStepState[] States(ISessionHandle handle) => [.. handle.ConnectSteps.Steps.Select(status => status.State)];
}
