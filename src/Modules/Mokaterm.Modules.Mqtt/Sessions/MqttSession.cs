using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Mqtt.Protocol;
using MQTTnet;
using MQTTnet.Exceptions;

namespace Mokaterm.Modules.Mqtt.Sessions;

/// <summary>
/// One broker connection. It exposes only <see cref="IMqttConnection"/>: an MQTT session has no terminal and no
/// file system, so the module's own view is the whole of it.
/// </summary>
internal sealed class MqttSession : IProtocolSession, IMqttConnection
{
	private readonly IMqttClient _client;
	private readonly MqttSecretCredentials? _credentials;
	private readonly MqttCertificateTrust? _trust;
	private readonly MqttSubscriptions _subscriptions;
	private readonly ILogger _logger;
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly CancellationTokenSource _closing = new();
	private readonly Lock _gate = new();
	private MqttConnectionInfo _info;
	private MqttConnectionState _state = MqttConnectionState.Connected;
	private string? _closeReason;
	private int _disposed;

	public MqttSession(
		IMqttClient client,
		MqttConnectionInfo info,
		MqttMessageStore messages,
		MqttSecretCredentials? credentials,
		MqttCertificateTrust? trust,
		ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(messages);
		_client = client;
		_info = info;
		Messages = messages;
		_credentials = credentials;
		_trust = trust;
		_logger = logger;
		_subscriptions = new MqttSubscriptions(client, info.Protocol, logger);
		_subscriptions.Changed += OnSubscriptionsChanged;
		_client.ApplicationMessageReceivedAsync += OnMessageAsync;
		_client.DisconnectedAsync += OnDisconnectedAsync;
	}

	public event Action? Changed;

	public Task Completion => _completion.Task;

	public MqttMessageStore Messages { get; }

	public MqttConnectionInfo Info
	{
		get
		{
			lock (_gate)
			{
				return _info;
			}
		}
	}

	public MqttConnectionState State
	{
		get
		{
			lock (_gate)
			{
				return _state;
			}
		}
	}

	public string? CloseReason
	{
		get
		{
			lock (_gate)
			{
				return _closeReason;
			}
		}
	}

	public IReadOnlyList<MqttSubscriptionStatus> Subscriptions => _subscriptions.Snapshot();

	public TFeature? GetFeature<TFeature>() where TFeature : class =>
		typeof(TFeature) == typeof(IMqttConnection) ? (TFeature)(object)this : null;

	/// <summary>Sends the subscriptions saved with the login. Called once, right after the session is created.</summary>
	public async Task StartAsync(IReadOnlyList<MqttSubscription> saved, CancellationToken cancellationToken)
	{
		_subscriptions.Seed(saved);
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closing.Token);
		await _subscriptions.SubscribeSeededAsync(linked.Token);
	}

	public Task<MqttSubscriptionStatus> SubscribeAsync(MqttSubscription subscription, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(subscription);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		return WithSessionTokenAsync(token => _subscriptions.SubscribeAsync(subscription, sessionOnly: true, token), cancellationToken);
	}

	public Task RemoveSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		return WithSessionTokenAsync(token => _subscriptions.RemoveAsync(subscriptionId, token), cancellationToken);
	}

	public async Task<MqttPublishOutcome> PublishAsync(MqttPublishRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		if (request.Validate() is { } invalid)
		{
			return MqttPublishOutcome.Failed(invalid);
		}

		if (State != MqttConnectionState.Connected)
		{
			return MqttPublishOutcome.Failed("The connection to the broker is closed.");
		}

		MqttApplicationMessage message = new MqttApplicationMessageBuilder()
			.WithTopic(request.Topic)
			.WithPayloadSegment(request.Payload)
			.WithQualityOfServiceLevel(MqttWire.ToWire(request.QualityOfService))
			.WithRetainFlag(request.Retain)
			.Build();

		try
		{
			MqttClientPublishResult result = await WithSessionTokenAsync(token => _client.PublishAsync(message, token), cancellationToken);
			if (result.IsSuccess)
			{
				return MqttPublishOutcome.Success(result.ReasonCode == MqttClientPublishReasonCode.NoMatchingSubscribers);
			}

			string reason = string.IsNullOrWhiteSpace(result.ReasonString) ? result.ReasonCode.ToString() : result.ReasonString;
			return MqttPublishOutcome.Failed($"The broker refused the message: {reason}");
		}
		catch (OperationCanceledException) when (Volatile.Read(ref _disposed) != 0)
		{
			return MqttPublishOutcome.Failed("The session closed while the message was on its way.");
		}
		catch (Exception ex) when (ex is MqttCommunicationException or MqttClientNotConnectedException or MqttProtocolViolationException or InvalidOperationException)
		{
			// The topic and the payload stay out of the log; the message is for the form beside the button.
			_logger.LogWarning("Publishing failed: {Error}", LogSafe.Describe(ex));
			return MqttPublishOutcome.Failed(ex.Message);
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_client.ApplicationMessageReceivedAsync -= OnMessageAsync;
		_client.DisconnectedAsync -= OnDisconnectedAsync;
		_subscriptions.Changed -= OnSubscriptionsChanged;
		await _closing.CancelAsync();

		try
		{
			if (_client.IsConnected)
			{
				// A DISCONNECT rather than a dropped socket, so the broker does not publish this client's will.
				await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), CancellationToken.None);
			}
		}
		catch (Exception ex)
		{
			_logger.LogDebug("Closing the broker connection failed: {Error}", LogSafe.Describe(ex));
		}

		SetClosed(reason: null);
		_client.Dispose();
		_credentials?.Dispose();
		_trust?.Dispose();
		_closing.Dispose();
		_completion.TrySetResult();
	}

	private Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs args)
	{
		MqttApplicationMessage message = args.ApplicationMessage;

		// The caps are applied here, on MQTTnet's receive loop, so a broker pushing megabytes a second is bounded
		// before anything is drawn. The payload is copied out of the sequence, which belongs to the receive buffer.
		Messages.Add(
			message.Topic,
			message.Payload,
			MqttWire.FromWire(message.QualityOfServiceLevel),
			message.Retain,
			message.ContentType);

		// Counted even when a cap dropped it: what a subscription brought in is not the same as what was kept.
		_subscriptions.CountMatch(message.Topic);
		return Task.CompletedTask;
	}

	private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			return Task.CompletedTask;
		}

		// MQTTnet's client does not reconnect, so this is the end of the session and the shell offers its own
		// reconnect. A reason string from the broker is worth showing; an exception message is not, it names hosts.
		string reason = Describe(args);
		_logger.LogInformation("The broker connection closed: {Reason}", args.Reason);
		SetClosed(reason);
		_subscriptions.MarkClosed();
		if (args.Reason == MqttClientDisconnectReason.NormalDisconnection)
		{
			_completion.TrySetResult();
		}
		else
		{
			_completion.TrySetException(new IOException(reason, args.Exception));
		}

		return Task.CompletedTask;
	}

	private static string Describe(MqttClientDisconnectedEventArgs args) => args.Reason switch
	{
		MqttClientDisconnectReason.NormalDisconnection => "The broker closed the connection.",
		MqttClientDisconnectReason.KeepAliveTimeout => "The broker saw no keep-alive and closed the connection.",
		MqttClientDisconnectReason.SessionTakenOver => "Another client connected with the same client id and took the session over.",
		MqttClientDisconnectReason.ServerShuttingDown => "The broker is shutting down.",
		MqttClientDisconnectReason.NotAuthorized => "The broker withdrew this client's permission.",
		MqttClientDisconnectReason.AdministrativeAction => "An administrator closed this connection on the broker.",
		_ => string.IsNullOrWhiteSpace(args.ReasonString)
			? $"The connection to the broker ended ({args.Reason})."
			: $"The connection to the broker ended ({args.Reason}): {args.ReasonString}",
	};

	private void SetClosed(string? reason)
	{
		lock (_gate)
		{
			if (_state == MqttConnectionState.Closed)
			{
				return;
			}

			_state = MqttConnectionState.Closed;
			_closeReason = reason;
		}

		Changed?.Invoke();
	}

	private void OnSubscriptionsChanged() => Changed?.Invoke();

	/// <summary>Runs work under the caller's token and the session's, so closing the session cuts a wait short.</summary>
	private async Task<T> WithSessionTokenAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
	{
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closing.Token);
		return await work(linked.Token);
	}

	private async Task WithSessionTokenAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
	{
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closing.Token);
		await work(linked.Token);
	}
}
