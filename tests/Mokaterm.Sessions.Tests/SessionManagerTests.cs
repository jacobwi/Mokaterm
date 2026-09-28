using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Sessions.Protocols;
using Mokaterm.Sessions.Tests.Fakes;

namespace Mokaterm.Sessions.Tests;

public sealed class SessionManagerTests : IAsyncDisposable
{
	private static readonly HostProfile Host = new() { Id = Guid.NewGuid(), Address = "10.0.0.5" };

	private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero));
	private readonly FakeConnectionRepository _repository = new();
	private readonly FakeCredentialStore _credentials = new();
	private readonly FakeUserInteraction _interaction = new();
	private readonly UncachedSettingsService _settings = new();
	private readonly ListLogger<SessionManager> _logger = new();
	private readonly FakeProtocolProvider _ssh = new(TestProtocols.Ssh);
	private readonly FakeProtocolProvider _sftp = new(TestProtocols.Sftp);
	private readonly FakeProtocolProvider _ftp = new(TestProtocols.Ftp);
	private readonly ConnectionProfile _connection;
	private readonly SessionManager _manager;

	public SessionManagerTests()
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
			new ProtocolRegistry([_ssh, _sftp, _ftp]),
			new FakeHostIdentityVerifier(),
			_interaction,
			_settings,
			_logger,
			_time);
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task OpenAsync_ConnectsInTheBackground_AndBindsTheTerminal()
	{
		int sessionsChanged = 0;
		_manager.SessionsChanged += () => Interlocked.Increment(ref sessionsChanged);

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);

		Assert.Same(handle, Assert.Single(_manager.Sessions));
		Assert.Same(handle, _manager.Find(handle.Id));
		Assert.Equal(1, sessionsChanged);
		Assert.Equal("abc@10.0.0.5", handle.Title);
		Assert.False(handle.IsTransient);
		ITerminalStream terminal = TerminalOf(handle);
		await WaitForStateAsync(handle, SessionState.Connected);

		ProtocolConnectContext context = Assert.Single(_ssh.Contexts);
		Assert.Equal(handle.Id, context.SessionId);
		Assert.Equal(22, context.Port);
		Assert.Equal(TerminalSize.Default, context.TerminalSize);
		Assert.Equal(_time.GetUtcNow(), handle.ConnectedAt);
		Assert.Null(handle.StatusMessage);
		FakeProtocolSession session = Assert.Single(_ssh.Sessions);
		Assert.Same(session, handle.Session);
		Assert.True(terminal.IsOpen);
		await Eventually.TrueAsync(() => _repository.MarkedConnected.Count == 1, "the connect time is recorded");
		Assert.Equal(_connection.Id, _repository.MarkedConnected[0].ConnectionId);

		RecordingSink sink = new();
		using IDisposable attachment = terminal.Attach(sink);
		ChannelOf(session).Emit("welcome");
		await Eventually.TrueAsync(() => sink.Text == "welcome", "remote output reaches an attached view");
	}

	[Fact]
	public async Task ResizeWhileConnecting_ReachesTheChannelOnceBound()
	{
		TaskCompletionSource<IProtocolSession> connect = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_ssh.OnConnect = (_, _) => connect.Task;
		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		await Eventually.TrueAsync(() => _ssh.ConnectCount == 1, "the provider is connecting");

		await TerminalOf(handle).ResizeAsync(new TerminalSize(132, 43), Ct);
		FakeProtocolSession session = _ssh.CreateSession();
		connect.SetResult(session);

		await WaitForStateAsync(handle, SessionState.Connected);
		FakeTerminalChannel channel = ChannelOf(session);
		await Eventually.TrueAsync(() => channel.Resizes.Contains(new TerminalSize(132, 43)), "the size reported while connecting is sent");
	}

	[Fact]
	public async Task OpenAsync_UnknownConnection_Throws() =>
		await Assert.ThrowsAsync<KeyNotFoundException>(() => _manager.OpenAsync(Guid.NewGuid(), cancellationToken: Ct));

	[Fact]
	public async Task ConnectFailure_ProtocolError_FailsWithItsFailureAndMessage()
	{
		_ssh.OnConnect = (_, _) => Task.FromException<IProtocolSession>(
			new ProtocolConnectException(ConnectFailure.AuthenticationFailed, "Permission denied (publickey)."));

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);

		await WaitForStateAsync(handle, SessionState.Failed);
		Assert.Equal(ConnectFailure.AuthenticationFailed, handle.Failure);
		Assert.Equal("Permission denied (publickey).", handle.StatusMessage);
		Assert.Null(handle.Session);
	}

	[Fact]
	public async Task ConnectFailure_UnexpectedError_FailsWithAGenericMessage_AndLogs()
	{
		_ssh.OnConnect = (_, _) => Task.FromException<IProtocolSession>(new IOException("socket 0x1f exploded"));

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);

		await WaitForStateAsync(handle, SessionState.Failed);
		Assert.Equal(ConnectFailure.Unknown, handle.Failure);
		Assert.False(string.IsNullOrWhiteSpace(handle.StatusMessage));
		Assert.DoesNotContain("0x1f", handle.StatusMessage, StringComparison.Ordinal);
		Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error);
	}

	[Fact]
	public async Task ConnectFailure_Cancellation_Disconnects()
	{
		_ssh.OnConnect = (_, _) => Task.FromCanceled<IProtocolSession>(new CancellationToken(canceled: true));

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);

		await WaitForStateAsync(handle, SessionState.Disconnected);
		Assert.Equal("Cancelled", handle.StatusMessage);
		Assert.Null(handle.Failure);
	}

	[Fact]
	public async Task CloseAsync_WhileConnecting_CancelsTheConnectAndRemovesTheSession()
	{
		_ssh.OnConnect = async (_, cancellationToken) =>
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return new FakeProtocolSession();
		};

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		await Eventually.TrueAsync(() => _ssh.ConnectCount == 1, "the provider is connecting");
		CancellationToken providerToken = Assert.Single(_ssh.Tokens);

		await _manager.CloseAsync(handle.Id);

		Assert.True(providerToken.IsCancellationRequested);
		Assert.Empty(_manager.Sessions);
		Assert.Null(_manager.Find(handle.Id));
		Assert.Equal(SessionState.Disconnected, handle.State);
		Assert.False(TerminalOf(handle).IsOpen);
	}

	[Fact]
	public async Task CloseAsync_WhenTheProviderIgnoresCancellation_DisposesTheLateSession()
	{
		TaskCompletionSource<IProtocolSession> connect = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_ssh.OnConnect = (_, _) => connect.Task;
		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		await Eventually.TrueAsync(() => _ssh.ConnectCount == 1, "the provider is connecting");

		await _manager.CloseAsync(handle.Id);
		FakeProtocolSession late = _ssh.CreateSession();
		connect.SetResult(late);

		await Eventually.TrueAsync(() => late.IsDisposed, "the session that arrived after closing is disposed");
		Assert.Null(handle.Session);
	}

	[Fact]
	public async Task OpenAsync_WithAVariantProtocol_UsesIt_AndSuffixesOnlyTheGeneratedTitle()
	{
		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, new SessionOpenOptions { ProtocolId = "SFTP" }, Ct);
		ISessionHandle titled = await _manager.OpenAsync(_connection.Id, new SessionOpenOptions { ProtocolId = "sftp", Title = "Files" }, Ct);

		Assert.Equal("sftp", handle.Protocol.Id);
		Assert.Equal("abc@10.0.0.5 (SFTP)", handle.Title);
		Assert.Equal("Files", titled.Title);
		Assert.Null(handle.Terminal);
		await WaitForStateAsync(handle, SessionState.Connected);
		await WaitForStateAsync(titled, SessionState.Connected);
		Assert.Equal(2, _sftp.ConnectCount);
		Assert.Equal(0, _ssh.ConnectCount);
	}

	[Fact]
	public async Task OpenAsync_WithAnUnrelatedProtocol_ThrowsWithoutOpening()
	{
		_ = await Assert.ThrowsAsync<ArgumentException>(() => _manager.OpenAsync(_connection.Id, new SessionOpenOptions { ProtocolId = "ftp" }, Ct));

		Assert.Empty(_manager.Sessions);
		Assert.Equal(0, _ftp.ConnectCount);
	}

	[Fact]
	public async Task OpenTransient_ConnectsWithoutRecordingOrSaving()
	{
		ConnectionProfile quick = new()
		{
			Id = Guid.NewGuid(),
			HostId = Host.Id,
			ProtocolId = "ssh",
			Username = "root",
			AuthenticationMethod = AuthenticationMethod.Password,
		};

		string? typedPassword = null;
		_interaction.AnswerCredentials("root", "toor", save: true);
		_ssh.OnConnect = async (context, cancellationToken) =>
		{
			using LoginCredentials? login = await context.Credentials.GetAsync(cancellationToken);
			typedPassword = login?.Password?.RevealString();
			return _ssh.CreateSession();
		};

		ISessionHandle handle = _manager.OpenTransient(Host, quick);

		Assert.True(handle.IsTransient);
		await WaitForStateAsync(handle, SessionState.Connected);
		Assert.Equal("toor", typedPassword);
		Assert.False(Assert.Single(_interaction.CredentialPrompts).OfferSave);
		await Eventually.StaysTrueAsync(
			() => _repository.MarkedConnected.Count == 0 && _repository.SavedConnections.Count == 0,
			"nothing about a transient session is stored");
		_ = Assert.Single(await _credentials.ListAsync(Ct));
	}

	[Fact]
	public async Task AdoptAsync_TurnsAQuickConnectSessionIntoTheLoginItWasSavedAs()
	{
		ConnectionProfile quick = new()
		{
			Id = Guid.NewGuid(),
			HostId = Host.Id,
			ProtocolId = "ssh",
			Username = "root",
			AuthenticationMethod = AuthenticationMethod.Password,
		};

		_interaction.AnswerCredentials("root", "toor", save: false);
		ISessionHandle handle = _manager.OpenTransient(Host, quick);
		await WaitForStateAsync(handle, SessionState.Connected);
		int changes = 0;
		handle.Changed += () => Interlocked.Increment(ref changes);

		HostProfile saved = new() { Id = Guid.NewGuid(), Address = "10.0.0.9", Name = "Web 1" };
		ConnectionProfile login = new()
		{
			Id = Guid.NewGuid(),
			HostId = saved.Id,
			ProtocolId = "ssh",
			Username = "abc",
			Label = "Web one",
			AuthenticationMethod = AuthenticationMethod.Password,
		};

		_repository.Add(saved, login);

		await _manager.AdoptAsync(handle.Id, login.Id, Ct);

		Assert.False(handle.IsTransient);
		Assert.Equal(login.Id, handle.Connection.Id);
		Assert.Equal("10.0.0.9", handle.Host.Address);
		Assert.Equal("Web one", handle.Title);
		Assert.Equal(SessionState.Connected, handle.State);
		Assert.Equal(1, Volatile.Read(ref changes));
	}

	[Fact]
	public async Task AdoptAsync_LeavesASavedSessionAlone()
	{
		ISessionHandle handle = await OpenConnectedAsync();
		Guid connectionId = handle.Connection.Id;

		await _manager.AdoptAsync(handle.Id, Guid.NewGuid(), Ct);

		Assert.False(handle.IsTransient);
		Assert.Equal(connectionId, handle.Connection.Id);
	}

	[Fact]
	public async Task AdoptAsync_UnknownConnection_Throws()
	{
		ConnectionProfile quick = new()
		{
			Id = Guid.NewGuid(),
			HostId = Host.Id,
			ProtocolId = "ssh",
			Username = "root",
			AuthenticationMethod = AuthenticationMethod.Password,
		};

		_interaction.AnswerCredentials("root", "toor", save: false);
		ISessionHandle handle = _manager.OpenTransient(Host, quick);

		await Assert.ThrowsAsync<KeyNotFoundException>(() => _manager.AdoptAsync(handle.Id, Guid.NewGuid(), Ct));
		Assert.True(handle.IsTransient);
	}

	[Fact]
	public async Task OpenAsync_PasswordTypedWithSave_IsStoredOnceConnected()
	{
		ConnectionProfile unsaved = _connection with { Id = Guid.NewGuid(), Username = "kim", CredentialId = null };
		_repository.Add(Host, unsaved);
		_interaction.AnswerCredentials("kim", "letmein", save: true);
		_ssh.OnConnect = async (context, cancellationToken) =>
		{
			using LoginCredentials? login = await context.Credentials.GetAsync(cancellationToken);
			return _ssh.CreateSession();
		};

		ISessionHandle handle = await _manager.OpenAsync(unsaved.Id, cancellationToken: Ct);

		await WaitForStateAsync(handle, SessionState.Connected);
		await Eventually.TrueAsync(() => _repository.SavedConnections.Count == 1, "the connection points at a new credential");
		Guid? credentialId = _repository.SavedConnections[0].CredentialId;
		Assert.NotNull(credentialId);
		Assert.Equal("letmein", _credentials.SecretOf(credentialId.Value)?.Password);
	}

	[Fact]
	public async Task SessionEnd_Clean_DisconnectsAndWritesANotice()
	{
		ISessionHandle handle = await OpenConnectedAsync();
		ITerminalStream terminal = TerminalOf(handle);
		RecordingSink sink = new();
		using IDisposable attachment = terminal.Attach(sink);

		Assert.Single(_ssh.Sessions).EndCleanly();

		await WaitForStateAsync(handle, SessionState.Disconnected);
		Assert.Equal("Connection closed", handle.StatusMessage);
		Assert.Null(handle.Session);
		Assert.Null(handle.Failure);
		await Eventually.TrueAsync(() => sink.Text.Contains("[mokaterm] connection closed", StringComparison.Ordinal), "the notice reaches the view");
		Assert.False(terminal.IsOpen);
	}

	[Fact]
	public async Task SessionEnd_Fault_FailsWithTheMessageAndFailure()
	{
		ISessionHandle handle = await OpenConnectedAsync();
		RecordingSink sink = new();
		using IDisposable attachment = TerminalOf(handle).Attach(sink);

		Assert.Single(_ssh.Sessions).Drop(new ProtocolConnectException(ConnectFailure.Timeout, "The server stopped responding."));

		await WaitForStateAsync(handle, SessionState.Failed);
		Assert.Equal(ConnectFailure.Timeout, handle.Failure);
		Assert.Equal("The server stopped responding.", handle.StatusMessage);
		await Eventually.TrueAsync(
			() => sink.Text.Contains("[mokaterm] connection lost: The server stopped responding.", StringComparison.Ordinal),
			"the notice reaches the view");
		Assert.Equal(1, _ssh.ConnectCount);
	}

	[Fact]
	public async Task AutoReconnect_AfterADrop_TriesThreeTimesWithDelays()
	{
		_settings.Set(new GeneralSettings { AutoReconnect = true, AutoReconnectDelaySeconds = 5 });
		ISessionHandle handle = await OpenConnectedAsync();
		_ssh.OnConnect = (_, _) => Task.FromException<IProtocolSession>(
			new ProtocolConnectException(ConnectFailure.HostUnreachable, "No route to host."));

		Assert.Single(_ssh.Sessions).Drop(new IOException("Connection reset by peer."));

		await Eventually.TrueAsync(() => StatusContains(handle, "Reconnecting in 5 s (attempt 1 of 3)"), "the first retry is scheduled");
		Assert.Equal(1, _ssh.ConnectCount);
		for (int connects = 2; connects <= 4; connects++)
		{
			int expected = connects;
			await Eventually.AdvanceUntilAsync(_time, TimeSpan.FromSeconds(1), () => _ssh.ConnectCount == expected, $"connect {expected} starts");
			if (expected < 4)
			{
				await Eventually.TrueAsync(() => StatusContains(handle, $"(attempt {expected} of 3)"), $"retry {expected} is scheduled");
			}
		}

		await Eventually.TrueAsync(
			() => handle.State == SessionState.Failed && handle.StatusMessage == "No route to host.",
			"the last failure is shown without another countdown");
		for (int i = 0; i < 10; i++)
		{
			_time.Advance(TimeSpan.FromSeconds(5));
		}

		await Eventually.StaysTrueAsync(() => _ssh.ConnectCount == 4, "no retry after the third");
	}

	[Fact]
	public async Task AutoReconnect_SkipsCleanEndsAndFailuresTheUserMustFix()
	{
		_settings.Set(new GeneralSettings { AutoReconnect = true, AutoReconnectDelaySeconds = 1 });
		ISessionHandle clean = await OpenConnectedAsync();
		ISessionHandle rejected = await OpenConnectedAsync();

		_ssh.Sessions[0].EndCleanly();
		_ssh.Sessions[1].Drop(new ProtocolConnectException(ConnectFailure.AuthenticationFailed, "The session expired."));

		await WaitForStateAsync(clean, SessionState.Disconnected);
		await WaitForStateAsync(rejected, SessionState.Failed);
		for (int i = 0; i < 10; i++)
		{
			_time.Advance(TimeSpan.FromSeconds(1));
		}

		await Eventually.StaysTrueAsync(
			() => _ssh.ConnectCount == 2 && rejected.StatusMessage == "The session expired." && clean.StatusMessage == "Connection closed",
			"neither session reconnects");
	}

	[Fact]
	public async Task ReconnectAsync_ReusesTheTerminalStream_AndRefreshesTheProfile()
	{
		ISessionHandle handle = await OpenConnectedAsync();
		ITerminalStream terminal = TerminalOf(handle);
		RecordingSink sink = new();
		using IDisposable attachment = terminal.Attach(sink);
		FakeProtocolSession first = Assert.Single(_ssh.Sessions);
		ChannelOf(first).Emit("first\r\n");
		await Eventually.TrueAsync(() => sink.Text.StartsWith("first", StringComparison.Ordinal), "output from the first connection arrives");
		await terminal.ResizeAsync(new TerminalSize(100, 40), Ct);

		first.EndCleanly();
		await WaitForStateAsync(handle, SessionState.Disconnected);
		await Eventually.TrueAsync(() => sink.Text.Contains("[mokaterm] connection closed", StringComparison.Ordinal), "the closing notice is written");
		_repository.Replace(_connection with { Label = "Renamed" });

		await _manager.ReconnectAsync(handle.Id, Ct);

		await WaitForStateAsync(handle, SessionState.Connected);
		Assert.Same(terminal, handle.Terminal);
		Assert.True(first.IsDisposed);
		Assert.Equal("Renamed", handle.Title);
		ProtocolConnectContext second = _ssh.Contexts[1];
		Assert.Equal("Renamed", second.Connection.Label);
		Assert.Equal(new TerminalSize(100, 40), second.TerminalSize);
		ChannelOf(_ssh.Sessions[1]).Emit("second");
		await Eventually.TrueAsync(() => sink.Text.EndsWith("second", StringComparison.Ordinal), "output from the new connection reaches the same view");
		Assert.Contains("[mokaterm] connection closed", sink.Text, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ReconnectAsync_WhileConnected_DoesNothing()
	{
		ISessionHandle handle = await OpenConnectedAsync();

		await _manager.ReconnectAsync(handle.Id, Ct);

		await Eventually.StaysTrueAsync(() => _ssh.ConnectCount == 1 && handle.State == SessionState.Connected, "no second connection starts");
	}

	[Fact]
	public async Task DisconnectAsync_WhileConnecting_CancelsTheConnectAndKeepsTheTab()
	{
		_ssh.OnConnect = async (_, cancellationToken) =>
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return new FakeProtocolSession();
		};

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		await Eventually.TrueAsync(() => _ssh.ConnectCount == 1, "the provider is connecting");
		CancellationToken providerToken = Assert.Single(_ssh.Tokens);

		await _manager.DisconnectAsync(handle.Id);

		Assert.True(providerToken.IsCancellationRequested);
		Assert.Same(handle, Assert.Single(_manager.Sessions));
		Assert.Equal(SessionState.Disconnected, handle.State);
		await Eventually.StaysTrueAsync(() => handle.StatusMessage == "Disconnected", "the cancelled attempt does not overwrite the state");
	}

	[Fact]
	public async Task DisconnectAsync_WhileConnected_ClosesTheConnection_KeepsTheScrollback_AndCanReconnect()
	{
		ISessionHandle handle = await OpenConnectedAsync();
		FakeProtocolSession first = Assert.Single(_ssh.Sessions);
		ITerminalStream terminal = TerminalOf(handle);
		RecordingSink sink = new();
		using IDisposable attachment = terminal.Attach(sink);
		ChannelOf(first).Emit("before\r\n");
		await Eventually.TrueAsync(() => sink.Text.StartsWith("before", StringComparison.Ordinal), "output arrives before the disconnect");

		await _manager.DisconnectAsync(handle.Id);

		Assert.True(first.IsDisposed);
		Assert.Same(handle, Assert.Single(_manager.Sessions));
		Assert.Equal(SessionState.Disconnected, handle.State);
		Assert.Null(handle.Session);

		// The notice is queued by the time the disconnect returns; the sink receives it on its own delivery task.
		await Eventually.TrueAsync(() => sink.Text.Contains("[mokaterm] disconnected", StringComparison.Ordinal), "the notice reaches the view");

		await _manager.ReconnectAsync(handle.Id, Ct);

		await WaitForStateAsync(handle, SessionState.Connected);
		Assert.Same(terminal, handle.Terminal);
		Assert.Equal(2, _ssh.ConnectCount);
		Assert.False(_ssh.Tokens[1].IsCancellationRequested);
	}

	[Fact]
	public async Task DisconnectAsync_DuringAutoReconnectCountdown_StopsIt()
	{
		_settings.Set(new GeneralSettings { AutoReconnect = true, AutoReconnectDelaySeconds = 5 });
		ISessionHandle handle = await OpenConnectedAsync();
		_ssh.OnConnect = (_, _) => Task.FromException<IProtocolSession>(
			new ProtocolConnectException(ConnectFailure.HostUnreachable, "No route to host."));

		Assert.Single(_ssh.Sessions).Drop(new IOException("Connection reset by peer."));
		await Eventually.TrueAsync(() => StatusContains(handle, "Reconnecting in 5 s (attempt 1 of 3)"), "the first retry is scheduled");

		await _manager.DisconnectAsync(handle.Id);

		// Disconnect only cancels the countdown; the loop it is waiting in replaces the status a moment later.
		await Eventually.TrueAsync(
			() => handle.State == SessionState.Failed && handle.StatusMessage == "Connection reset by peer.",
			"the countdown text is replaced by the reason");

		for (int i = 0; i < 10; i++)
		{
			_time.Advance(TimeSpan.FromSeconds(5));
		}

		await Eventually.StaysTrueAsync(
			() => _ssh.ConnectCount == 1 && handle.State == SessionState.Failed && handle.StatusMessage == "Connection reset by peer.",
			"no retry runs after the countdown is stopped");
	}

	[Fact]
	public async Task LateResultOfADisconnectedAttempt_LeavesTheNewConnectionsTerminalAlone()
	{
		// The first attempt ignores cancellation, like a library stuck in a synchronous handshake.
		TaskCompletionSource<IProtocolSession> slow = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_ssh.OnConnect = (_, _) => slow.Task;
		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		await Eventually.TrueAsync(() => _ssh.ConnectCount == 1, "the first attempt is connecting");

		await _manager.DisconnectAsync(handle.Id);
		_ssh.OnConnect = (_, _) => Task.FromResult<IProtocolSession>(_ssh.CreateSession());
		await _manager.ReconnectAsync(handle.Id, Ct);
		await WaitForStateAsync(handle, SessionState.Connected);
		FakeProtocolSession current = Assert.Single(_ssh.Sessions);
		ITerminalStream terminal = TerminalOf(handle);
		RecordingSink sink = new();
		using IDisposable attachment = terminal.Attach(sink);

		FakeProtocolSession late = _ssh.CreateSession();
		slow.SetResult(late);
		await Eventually.TrueAsync(() => late.IsDisposed, "the session that arrived late is disposed");
		await Eventually.StaysTrueAsync(() => terminal.IsOpen, "the live connection keeps the terminal");

		ChannelOf(current).Emit("still here");
		await Eventually.TrueAsync(() => sink.Text.EndsWith("still here", StringComparison.Ordinal), "the live connection's output reaches the view");
		await terminal.SendTextAsync("ls\r", Ct);
		Assert.Equal("ls\r", ChannelOf(current).InputText);
		Assert.Same(current, handle.Session);
		Assert.Equal(SessionState.Connected, handle.State);
	}

	[Fact]
	public async Task AutoReconnect_StopsWhenTheUserReconnectedDuringTheCountdown()
	{
		_settings.Set(new GeneralSettings { AutoReconnect = true, AutoReconnectDelaySeconds = 5 });
		ISessionHandle handle = await OpenConnectedAsync();
		Assert.Single(_ssh.Sessions).Drop(new IOException("Connection reset by peer."));
		await Eventually.TrueAsync(() => StatusContains(handle, "(attempt 1 of 3)"), "the first retry is scheduled");

		// The user reconnects by hand and turns down the login, which must not be asked for again by the old countdown.
		_ssh.OnConnect = (_, _) => Task.FromException<IProtocolSession>(
			new ProtocolConnectException(ConnectFailure.AuthenticationFailed, "The server rejected the login."));
		await _manager.ReconnectAsync(handle.Id, Ct);
		await Eventually.TrueAsync(() => handle.Failure == ConnectFailure.AuthenticationFailed, "the manual reconnect fails on the login");

		for (int i = 0; i < 10; i++)
		{
			_time.Advance(TimeSpan.FromSeconds(5));
		}

		await Eventually.StaysTrueAsync(
			() => _ssh.ConnectCount == 2 && handle.StatusMessage == "The server rejected the login.",
			"the old countdown neither connects again nor replaces the failure",
			window: TimeSpan.FromMilliseconds(500));
	}

	[Fact]
	public async Task ASessionWhoseFeaturesThrow_IsDisposedAndFails()
	{
		ThrowingFeatureSession broken = new();
		_ssh.OnConnect = (_, _) => Task.FromResult<IProtocolSession>(broken);

		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);

		await WaitForStateAsync(handle, SessionState.Failed);
		await Eventually.TrueAsync(() => broken.IsDisposed, "the session is disposed");
		Assert.Equal(ConnectFailure.Unknown, handle.Failure);
		Assert.Null(handle.Session);
	}

	[Fact]
	public async Task CloseAsync_DisposesTheSessionAndTheTerminalStream()
	{
		ISessionHandle handle = await OpenConnectedAsync();
		FakeProtocolSession session = Assert.Single(_ssh.Sessions);
		ITerminalStream terminal = TerminalOf(handle);
		int sessionsChanged = 0;
		_manager.SessionsChanged += () => Interlocked.Increment(ref sessionsChanged);

		await _manager.CloseAsync(handle.Id);
		await _manager.CloseAsync(handle.Id);

		Assert.True(session.IsDisposed);
		Assert.Empty(_manager.Sessions);
		Assert.Equal(1, Volatile.Read(ref sessionsChanged));
		Assert.Equal(SessionState.Disconnected, handle.State);
		Assert.Null(handle.Session);
		Assert.False(terminal.IsOpen);
		await Eventually.StaysTrueAsync(() => handle.State == SessionState.Disconnected && !terminal.IsOpen, "a closed session stays closed");
	}

	[Fact]
	public async Task CloseAsync_WhenDisposeHangs_GivesUpAfterTheTimeout()
	{
		TaskCompletionSource stuck = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_ssh.OnConnect = (_, _) => Task.FromResult<IProtocolSession>(new FakeProtocolSession(new FakeTerminalChannel()) { DisposeBlocker = stuck.Task });
		ISessionHandle handle = await OpenConnectedAsync();

		Task close = _manager.CloseAsync(handle.Id);
		await Eventually.StaysTrueAsync(() => !close.IsCompleted, "closing waits for the session to dispose");
		await Eventually.AdvanceUntilAsync(_time, TimeSpan.FromSeconds(1), () => close.IsCompleted, "closing gives up after the timeout");

		await close;
		Assert.Empty(_manager.Sessions);
		Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("did not close", StringComparison.Ordinal));
		stuck.SetResult();
	}

	[Fact]
	public async Task DisposeAsync_ClosesEverySession()
	{
		_ = await OpenConnectedAsync();
		_ = await OpenConnectedAsync();

		await _manager.DisposeAsync();

		Assert.Empty(_manager.Sessions);
		Assert.Equal(2, _ssh.Sessions.Count);
		Assert.All(_ssh.Sessions, session => Assert.True(session.IsDisposed));
		_ = await Assert.ThrowsAsync<ObjectDisposedException>(() => _manager.OpenAsync(_connection.Id, cancellationToken: Ct));
	}

	public ValueTask DisposeAsync() => _manager.DisposeAsync();

	private static ITerminalStream TerminalOf(ISessionHandle handle)
	{
		Assert.NotNull(handle.Terminal);
		return handle.Terminal;
	}

	private static FakeTerminalChannel ChannelOf(FakeProtocolSession session) => Assert.IsType<FakeTerminalChannel>(session.Channel);

	private static bool StatusContains(ISessionHandle handle, string text) =>
		handle.StatusMessage?.Contains(text, StringComparison.Ordinal) == true;

	private static Task WaitForStateAsync(ISessionHandle handle, SessionState state) =>
		Eventually.TrueAsync(() => handle.State == state, $"the session is {state}");

	private async Task<ISessionHandle> OpenConnectedAsync()
	{
		ISessionHandle handle = await _manager.OpenAsync(_connection.Id, cancellationToken: Ct);
		await WaitForStateAsync(handle, SessionState.Connected);
		return handle;
	}

	/// <summary>A provider bug: the session connected, then asking it for a feature throws.</summary>
	private sealed class ThrowingFeatureSession : IProtocolSession
	{
		private int _disposed;

		public Task Completion { get; } = new TaskCompletionSource().Task;

		public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

		public TFeature? GetFeature<TFeature>() where TFeature : class => throw new InvalidOperationException("The feature table is broken.");

		public ValueTask DisposeAsync()
		{
			_ = Interlocked.Exchange(ref _disposed, 1);
			return ValueTask.CompletedTask;
		}
	}
}
