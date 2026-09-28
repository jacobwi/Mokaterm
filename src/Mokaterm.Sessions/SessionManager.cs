using System.Globalization;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Sessions.Terminal;

namespace Mokaterm.Sessions;

/// <summary>
/// Opens, reconnects and closes the sessions of one UI scope. Connecting runs in the background and reports through
/// each session's handle.
/// </summary>
internal sealed class SessionManager : ISessionManager, IDisposable
{
	internal const int MaxAutoReconnectAttempts = 3;

	/// <summary>How long closing waits for a protocol session to dispose before leaving it to finish on its own.</summary>
	internal static readonly TimeSpan SessionDisposeTimeout = TimeSpan.FromSeconds(5);

	/// <summary>How long an ended session waits for the terminal pump to flush the last output before the notice.</summary>
	internal static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(1);

	private const string UnexpectedFailure = "The connection failed because of an unexpected error.";

	private readonly IConnectionRepository _repository;
	private readonly ICredentialStore _credentialStore;
	private readonly IProtocolRegistry _registry;
	private readonly IHostIdentityVerifier _hostVerifier;
	private readonly IUserInteraction _interaction;
	private readonly ISettingsService _settings;
	private readonly ILogger<SessionManager> _logger;
	private readonly TimeProvider _timeProvider;
	private readonly Lock _lock = new();
	private readonly List<SessionHandle> _sessions = [];
	private SessionHandle[] _snapshot = [];
	private bool _disposed;

	public SessionManager(
		IConnectionRepository repository,
		ICredentialStore credentialStore,
		IProtocolRegistry registry,
		IHostIdentityVerifier hostVerifier,
		IUserInteraction interaction,
		ISettingsService settings,
		ILogger<SessionManager> logger,
		TimeProvider timeProvider)
	{
		_repository = repository;
		_credentialStore = credentialStore;
		_registry = registry;
		_hostVerifier = hostVerifier;
		_interaction = interaction;
		_settings = settings;
		_logger = logger;
		_timeProvider = timeProvider;
	}

	public event Action? SessionsChanged;

	public IReadOnlyList<ISessionHandle> Sessions
	{
		get
		{
			lock (_lock)
			{
				return _snapshot;
			}
		}
	}

	public ISessionHandle? Find(Guid sessionId) => FindHandle(sessionId);

	public async Task<ISessionHandle> OpenAsync(Guid connectionId, SessionOpenOptions? options = null, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		(HostProfile host, ConnectionProfile connection) = await LoadProfilesAsync(connectionId, cancellationToken);
		return Open(host, connection, options, isTransient: false);
	}

	public ISessionHandle OpenTransient(HostProfile host, ConnectionProfile connection, SessionOpenOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(host);
		ArgumentNullException.ThrowIfNull(connection);
		return Open(host, connection, options, isTransient: true);
	}

	public async Task ReconnectAsync(Guid sessionId, CancellationToken cancellationToken = default)
	{
		SessionHandle? handle = FindHandle(sessionId);
		if (handle is null || !ISessionHandle.CanReconnect(handle.State))
		{
			return;
		}

		if (!handle.IsTransient)
		{
			(HostProfile host, ConnectionProfile connection) = await LoadProfilesAsync(handle.Connection.Id, cancellationToken);
			ProtocolDescriptor saved = _registry.Get(connection.ProtocolId).Descriptor;
			if (!IsSameOrVariant(saved, handle.Protocol))
			{
				throw new InvalidOperationException($"The connection now uses {saved.DisplayName}. Open it again to use that protocol.");
			}

			handle.UpdateProfiles(host, connection);
		}

		StartConnect(handle);
	}

	public async Task AdoptAsync(Guid sessionId, Guid connectionId, CancellationToken cancellationToken = default)
	{
		SessionHandle? handle = FindHandle(sessionId);
		if (handle is null || !handle.IsTransient)
		{
			return;
		}

		(HostProfile host, ConnectionProfile connection) = await LoadProfilesAsync(connectionId, cancellationToken);
		handle.Adopt(host, connection);

		// The tab list itself did not change, but what a tab offers did: saving, editing and the file browser variant.
		RaiseSessionsChanged();
	}

	public async Task DisconnectAsync(Guid sessionId)
	{
		SessionHandle? handle = FindHandle(sessionId);
		if (handle is null || !handle.TryDisconnect(out IProtocolSession? session) || session is null)
		{
			return;
		}

		await DisposeSessionAsync(handle.Id, session);
		await WriteNoticeAsync(handle, "disconnected");
	}

	public async Task CloseAsync(Guid sessionId)
	{
		SessionHandle? handle = FindHandle(sessionId);
		if (handle is null || !handle.TryClose(out IProtocolSession? session))
		{
			return;
		}

		if (session is not null)
		{
			await DisposeSessionAsync(handle.Id, session);
		}

		if (handle.TerminalStream is { } terminal)
		{
			await terminal.DisposeAsync();
		}

		lock (_lock)
		{
			_ = _sessions.Remove(handle);
			_snapshot = [.. _sessions];
		}

		handle.Dispose();
		RaiseSessionsChanged();
	}

	public Task CloseAllAsync()
	{
		SessionHandle[] handles;
		lock (_lock)
		{
			handles = _snapshot;
		}

		return Task.WhenAll(handles.Select(handle => CloseAsync(handle.Id)));
	}

	public async ValueTask DisposeAsync()
	{
		if (TryMarkDisposed())
		{
			await CloseAllAsync();
		}
	}

	public void Dispose()
	{
		if (TryMarkDisposed())
		{
			// A synchronous scope disposal cannot wait for network closes, so they finish in the background.
			_ = CloseAllAsync();
		}
	}

	private static bool IsSameOrVariant(ProtocolDescriptor saved, ProtocolDescriptor used) =>
		string.Equals(saved.Id, used.Id, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(used.VariantOf, saved.Id, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(saved.VariantOf, used.Id, StringComparison.OrdinalIgnoreCase);

	private static string MessageOf(Exception exception) =>
		string.IsNullOrWhiteSpace(exception.Message) ? "The connection failed." : exception.Message;

	/// <summary>Keeps a notice on one dim line: remote text in an exception message must not move the cursor or restyle output.</summary>
	private static string Printable(string text) =>
		string.Create(text.Length, text, static (span, source) =>
		{
			for (int i = 0; i < source.Length; i++)
			{
				span[i] = char.IsControl(source[i]) ? ' ' : source[i];
			}
		});

	private SessionHandle Open(HostProfile host, ConnectionProfile connection, SessionOpenOptions? options, bool isTransient)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ProtocolDescriptor saved = _registry.Get(connection.ProtocolId).Descriptor;
		ProtocolDescriptor protocol = ResolveProtocol(saved, options);
		bool isVariant = !string.Equals(protocol.Id, saved.Id, StringComparison.OrdinalIgnoreCase);
		string? title = options?.Title is { } requestedTitle && !string.IsNullOrWhiteSpace(requestedTitle) ? requestedTitle : null;

		// The stream exists before connecting so a view can attach and report its size right away.
		TerminalStream? terminal = protocol.Has(ProtocolCapabilities.Terminal) ? new TerminalStream(ReplayBufferBytes(), _logger) : null;
		SessionHandle handle = new(
			host,
			connection,
			protocol,
			isTransient,
			_timeProvider.GetUtcNow(),
			terminal,
			title,
			isVariant ? $" ({protocol.DisplayName})" : null,
			_logger);

		bool added = false;
		lock (_lock)
		{
			if (!_disposed)
			{
				_sessions.Add(handle);
				_snapshot = [.. _sessions];
				added = true;
			}
		}

		if (!added)
		{
			handle.Dispose();
		}

		ObjectDisposedException.ThrowIf(!added, this);
		RaiseSessionsChanged();
		StartConnect(handle);
		return handle;
	}

	private ProtocolDescriptor ResolveProtocol(ProtocolDescriptor saved, SessionOpenOptions? options)
	{
		string? requestedId = options?.ProtocolId;
		if (string.IsNullOrWhiteSpace(requestedId) || string.Equals(requestedId, saved.Id, StringComparison.OrdinalIgnoreCase))
		{
			return saved;
		}

		ProtocolDescriptor requested = _registry.Find(requestedId)?.Descriptor
			?? throw new ArgumentException($"No module registered the protocol '{requestedId}'.", nameof(options));
		return IsSameOrVariant(saved, requested)
			? requested
			: throw new ArgumentException($"A {saved.DisplayName} connection cannot be opened as {requested.DisplayName}.", nameof(options));
	}

	// The section's own range, not one of its own: a settings file nobody sanitized would otherwise buy a session a
	// buffer the page never offered.
	private int ReplayBufferBytes() =>
		Math.Clamp(
			_settings.Get<TerminalSettings>().ReplayBufferKilobytes,
			TerminalSettings.MinReplayBufferKilobytes,
			TerminalSettings.MaxReplayBufferKilobytes) * 1024;

	private async Task<(HostProfile Host, ConnectionProfile Connection)> LoadProfilesAsync(Guid connectionId, CancellationToken cancellationToken)
	{
		ConnectionCatalog catalog = await _repository.GetCatalogAsync(cancellationToken);
		ConnectionProfile connection = catalog.FindConnection(connectionId)
			?? throw new KeyNotFoundException($"No connection with id {connectionId}.");
		HostProfile host = catalog.FindHost(connection.HostId)
			?? throw new KeyNotFoundException($"The host of connection {connectionId} no longer exists.");
		return (host, connection);
	}

	// Task.Run keeps the connect flow, its continuations and the provider off a caller's synchronization context,
	// such as a Blazor renderer.
	private void StartConnect(SessionHandle handle) => _ = Task.Run(() => ConnectAsync(handle, afterAttempt: null));

	/// <summary>
	/// Runs one connect attempt. Never throws: the outcome lands on the handle. <paramref name="afterAttempt"/> is set by
	/// the automatic reconnect, which only goes ahead while no other attempt started since the one it follows.
	/// </summary>
	private async Task<ConnectOutcome> ConnectAsync(SessionHandle handle, int? afterAttempt)
	{
		HostProfile host = handle.Host;
		ConnectionProfile connection = handle.Connection;
		ProtocolDescriptor protocol = handle.Protocol;
		int port = connection.Port ?? protocol.DefaultPort;
		int attempt = handle.TryBeginAttempt(
			string.Create(CultureInfo.CurrentCulture, $"Connecting to {host.Address}:{port}"),
			afterAttempt,
			out IProtocolSession? endedSession,
			out CancellationToken cancellationToken);
		if (attempt == 0)
		{
			return ConnectOutcome.NotStarted;
		}

		if (endedSession is not null)
		{
			await DisposeSessionAsync(handle.Id, endedSession);
		}

		TerminalStream? terminal = handle.TerminalStream;
		TerminalSize size = terminal?.Size is { IsValid: true } lastSize ? lastSize : TerminalSize.Default;
		using CredentialSource credentials = new(connection, host, protocol, handle.IsTransient, _credentialStore, _repository, _interaction, _logger);

		IProtocolSession session;
		try
		{
			ProtocolConnectContext context = new()
			{
				SessionId = handle.Id,
				Host = host,
				Connection = connection,
				Port = port,
				Credentials = credentials,
				HostVerifier = _hostVerifier,
				Interaction = _interaction,
				Status = new InlineProgress<string>(message => handle.ReportStatus(attempt, message)),
				TerminalSize = size,
				Resolver = new ConnectionResolver(_repository, _credentialStore, _registry, _interaction, _logger),
			};

			session = await _registry.Get(protocol.Id).ConnectAsync(context, cancellationToken);
		}
		catch (ProtocolConnectException ex)
		{
			_ = handle.TryMarkEnded(attempt, SessionState.Failed, MessageOf(ex), ex.Failure);
			return ConnectOutcome.Ended(attempt);
		}
		catch (OperationCanceledException)
		{
			_ = handle.TryMarkEnded(attempt, SessionState.Disconnected, "Cancelled", null);
			return ConnectOutcome.Ended(attempt);
		}
		catch (VaultLockedException ex)
		{
			_ = handle.TryMarkEnded(attempt, SessionState.Failed, ex.Message, ConnectFailure.Unknown);
			return ConnectOutcome.Ended(attempt);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Session {SessionId} could not connect with protocol {ProtocolId}.", handle.Id, protocol.Id);
			_ = handle.TryMarkEnded(attempt, SessionState.Failed, UnexpectedFailure, ConnectFailure.Unknown);
			return ConnectOutcome.Ended(attempt);
		}

		// A provider that ignored cancellation can finish after the user disconnected, or after a newer attempt already
		// connected; only the attempt still waiting gets the terminal.
		Task outputDrained = Task.CompletedTask;
		if (terminal is not null && handle.IsConnecting(attempt))
		{
			try
			{
				if (session.GetFeature<ITerminalChannel>() is { } channel)
				{
					outputDrained = terminal.Bind(channel, size, attempt);
				}
				else
				{
					_logger.LogWarning("Session {SessionId} has no terminal channel although protocol {ProtocolId} declares one.", handle.Id, protocol.Id);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Session {SessionId} connected with protocol {ProtocolId} but its terminal could not be opened.", handle.Id, protocol.Id);
				await DisposeSessionAsync(handle.Id, session);
				_ = handle.TryMarkEnded(attempt, SessionState.Failed, UnexpectedFailure, ConnectFailure.Unknown);
				return ConnectOutcome.Ended(attempt);
			}
		}

		DateTimeOffset connectedAt = _timeProvider.GetUtcNow();
		if (!handle.TryMarkConnected(attempt, session, connectedAt))
		{
			// The tab closed or disconnected while connecting and the provider finished anyway.
			await DisposeSessionAsync(handle.Id, session);
			return ConnectOutcome.Ended(attempt);
		}

		_ = Task.Run(() => WatchAsync(handle, attempt, session, outputDrained));
		if (!handle.IsTransient)
		{
			// The login already succeeded, so a disconnect right away must not undo saving it; only closing the tab does.
			await credentials.SavePendingAsync(handle.Lifetime);
			try
			{
				await _repository.MarkConnectedAsync(connection.Id, connectedAt, handle.Lifetime);
			}
			catch (Exception ex)
			{
				_logger.LogDebug(ex, "Recording the connect time of connection {ConnectionId} failed.", connection.Id);
			}
		}

		return ConnectOutcome.Succeeded(attempt);
	}

	private async Task WatchAsync(SessionHandle handle, int attempt, IProtocolSession session, Task outputDrained)
	{
		Exception? fault = null;
		try
		{
			await session.Completion;
		}
		catch (OperationCanceledException)
		{
			// A cancelled completion is the session closing itself, not a drop.
		}
		catch (Exception ex)
		{
			fault = ex;
		}

		// Output read just before the end should reach the views ahead of the notice.
		await WaitQuietlyAsync(outputDrained, OutputDrainTimeout);

		ConnectFailure? failure = (fault as ProtocolConnectException)?.Failure;
		string reason = fault is null ? "Connection closed" : MessageOf(fault);
		SessionState state = fault is null ? SessionState.Disconnected : SessionState.Failed;
		if (!handle.TryMarkEnded(attempt, state, reason, failure))
		{
			return;
		}

		await WriteNoticeAsync(handle, fault is null ? "connection closed" : $"connection lost: {reason}");

		// A clean end (the user typed exit) is not a drop; neither is a login or host key the user must fix.
		if (fault is not null && failure is not (ConnectFailure.AuthenticationFailed or ConnectFailure.HostIdentityRejected))
		{
			await AutoReconnectAsync(handle, attempt, reason);
		}
	}

	/// <summary>
	/// Retries after a drop. Every step is tied to the last attempt this loop knows of, so a reconnect the user started
	/// meanwhile owns the session from then on: the loop neither connects again nor overwrites what that attempt shows.
	/// </summary>
	private async Task AutoReconnectAsync(SessionHandle handle, int droppedAttempt, string reason)
	{
		int last = droppedAttempt;
		for (int number = 1; number <= MaxAutoReconnectAttempts; number++)
		{
			GeneralSettings general = _settings.Get<GeneralSettings>();
			if (!general.AutoReconnect)
			{
				return;
			}

			int delaySeconds = Math.Max(0, general.AutoReconnectDelaySeconds);
			string status = string.Create(
				CultureInfo.CurrentCulture,
				$"{reason.TrimEnd('.')}. Reconnecting in {delaySeconds} s (attempt {number} of {MaxAutoReconnectAttempts}).");
			if (!handle.TrySetEndedStatus(status, last))
			{
				return;
			}

			try
			{
				await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _timeProvider, handle.ActivityToken);
			}
			catch (OperationCanceledException)
			{
				// Disconnect stopped the countdown: show why the session ended instead of a countdown that will not run.
				_ = handle.TrySetEndedStatus(reason, last);
				return;
			}

			ConnectOutcome outcome = await ConnectAsync(handle, afterAttempt: last);
			if (outcome.Attempt == 0 || outcome.Connected)
			{
				return;
			}

			last = outcome.Attempt;

			// Stop when the user cancelled, or has to fix the login or host key first.
			if (handle.State != SessionState.Failed
				|| handle.Failure is ConnectFailure.AuthenticationFailed or ConnectFailure.HostIdentityRejected or ConnectFailure.Cancelled)
			{
				return;
			}

			reason = handle.StatusMessage ?? reason;
		}
	}

	private static async Task WriteNoticeAsync(SessionHandle handle, string notice)
	{
		if (handle.TerminalStream is not { } terminal)
		{
			return;
		}

		try
		{
			await terminal.WriteLocalAsync($"\r\n[2m[mokaterm] {Printable(notice)}[0m\r\n", handle.Lifetime);
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task WaitQuietlyAsync(Task task, TimeSpan timeout)
	{
		try
		{
			await task.WaitAsync(timeout, _timeProvider);
		}
		catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
		{
		}
	}

	private async Task DisposeSessionAsync(Guid sessionId, IProtocolSession session)
	{
		// Some protocol libraries close synchronously over the network, so disposal runs on the pool where the wait
		// can give up on it.
		Task disposal = Task.Run(() => session.DisposeAsync().AsTask());
		try
		{
			await disposal.WaitAsync(SessionDisposeTimeout, _timeProvider);
		}
		catch (TimeoutException)
		{
			_logger.LogWarning(
				"Session {SessionId} did not close within {Seconds} seconds and keeps closing in the background.",
				sessionId,
				SessionDisposeTimeout.TotalSeconds);
			_ = disposal.ContinueWith(
				task => _logger.LogWarning(task.Exception, "Closing session {SessionId} failed.", sessionId),
				CancellationToken.None,
				TaskContinuationOptions.OnlyOnFaulted,
				TaskScheduler.Default);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Closing session {SessionId} failed.", sessionId);
		}
	}

	private SessionHandle? FindHandle(Guid sessionId)
	{
		lock (_lock)
		{
			return Array.Find(_snapshot, handle => handle.Id == sessionId);
		}
	}

	private bool TryMarkDisposed()
	{
		lock (_lock)
		{
			if (_disposed)
			{
				return false;
			}

			_disposed = true;
			return true;
		}
	}

	private void RaiseSessionsChanged() => EventRaiser.Raise(SessionsChanged, _logger, nameof(SessionsChanged));

	/// <summary>What one call to <see cref="ConnectAsync"/> did. <see cref="Attempt"/> is 0 when no attempt started.</summary>
	private readonly record struct ConnectOutcome(int Attempt, bool Connected)
	{
		public static ConnectOutcome NotStarted => default;

		public static ConnectOutcome Ended(int attempt) => new(attempt, Connected: false);

		public static ConnectOutcome Succeeded(int attempt) => new(attempt, Connected: true);
	}
}
