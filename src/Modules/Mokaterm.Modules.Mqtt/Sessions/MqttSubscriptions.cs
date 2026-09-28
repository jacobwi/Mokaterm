using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Modules.Mqtt.Protocol;
using MQTTnet;
using MQTTnet.Exceptions;

namespace Mokaterm.Modules.Mqtt.Sessions;

/// <summary>
/// The subscriptions of one live connection: the ones saved with the login and the ones the view added for this
/// session only. A broker that refuses a filter leaves the row in place with the reason on it, because a refused
/// subscription is something to fix, not something that should take the session down.
/// </summary>
internal sealed class MqttSubscriptions
{
	private readonly IMqttClient _client;
	private readonly bool _retainAsPublished;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly List<Entry> _entries = [];

	/// <param name="protocol">
	/// What the connection speaks. Only MQTT 5 has the "retain as published" subscription option, and without it a
	/// broker clears the retain flag on every message it forwards to a subscription that already existed, so the
	/// explorer could not tell a retained value from a fresh one. MQTTnet throws for the option on an older version,
	/// which is why this is decided once here.
	/// </param>
	public MqttSubscriptions(IMqttClient client, MqttProtocolLevel protocol, ILogger logger)
	{
		_client = client;
		_retainAsPublished = protocol == MqttProtocolLevel.V500;
		_logger = logger;
	}

	/// <summary>Raised after any subscription changed. Handlers may run on any thread.</summary>
	public event Action? Changed;

	public IReadOnlyList<MqttSubscriptionStatus> Snapshot()
	{
		lock (_gate)
		{
			return [.. _entries.Select(entry => entry.Status)];
		}
	}

	/// <summary>Puts the saved subscriptions in the list, in order, before any of them is sent.</summary>
	public void Seed(IReadOnlyList<MqttSubscription> saved)
	{
		ArgumentNullException.ThrowIfNull(saved);
		lock (_gate)
		{
			foreach (MqttSubscription subscription in saved)
			{
				if (subscription.Validate() is null && !_entries.Exists(entry => entry.Status.Subscription.Id == subscription.Id))
				{
					_entries.Add(new Entry(new MqttSubscriptionStatus
					{
						Subscription = subscription,
						State = MqttSubscriptionState.Idle,
					}));
				}
			}
		}

		Changed?.Invoke();
	}

	/// <summary>Sends every saved subscription marked to open with the session. Failures stay on their own rows.</summary>
	public async Task SubscribeSeededAsync(CancellationToken cancellationToken)
	{
		foreach (MqttSubscriptionStatus status in Snapshot())
		{
			if (status.Subscription.SubscribeWithSession)
			{
				await SubscribeAsync(status.Subscription, sessionOnly: false, cancellationToken);
			}
		}
	}

	/// <summary>
	/// Subscribes, adding the row when the connection does not carry it. One SUBSCRIBE per call, because a broker
	/// answers each filter with its own code and one refused filter must not hide the others.
	/// </summary>
	public async Task<MqttSubscriptionStatus> SubscribeAsync(MqttSubscription subscription, bool sessionOnly, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(subscription);
		if (subscription.Validate() is { } invalid)
		{
			return Update(subscription, sessionOnly, status => status with { State = MqttSubscriptionState.Failed, Error = invalid });
		}

		Update(subscription, sessionOnly, status => status with { State = MqttSubscriptionState.Subscribing, Error = null });
		try
		{
			MqttClientSubscribeResult result = await _client.SubscribeAsync(
				new MqttClientSubscribeOptionsBuilder()
					.WithTopicFilter(
						subscription.TopicFilter,
						MqttWire.ToWire(subscription.QualityOfService),
						noLocal: false,
						retainAsPublished: _retainAsPublished)
					.Build(),
				cancellationToken);

			MqttClientSubscribeResultItem? item = result.Items.FirstOrDefault();
			MqttQos? granted = item is null ? null : MqttWire.Granted(item.ResultCode);
			return granted is null
				? Update(subscription, sessionOnly, status => status with
				{
					State = MqttSubscriptionState.Failed,
					Error = Describe(item?.ResultCode),
				})
				: Update(subscription, sessionOnly, status => status with
				{
					State = MqttSubscriptionState.Active,
					GrantedQos = granted,
					Error = null,
				});
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return Update(subscription, sessionOnly, status => status with { State = MqttSubscriptionState.Idle, Error = null });
		}
		catch (Exception ex) when (ex is MqttCommunicationException
			or MqttProtocolViolationException
			or MqttClientNotConnectedException
			or NotSupportedException
			or InvalidOperationException)
		{
			_logger.LogWarning("Subscribing failed: {Error}", LogSafe.Describe(ex));
			return Update(subscription, sessionOnly, status => status with { State = MqttSubscriptionState.Failed, Error = ex.Message });
		}
	}

	/// <summary>Unsubscribes and drops the row. A filter the broker never took is dropped without a packet.</summary>
	public async Task RemoveAsync(Guid subscriptionId, CancellationToken cancellationToken)
	{
		MqttSubscriptionStatus? status;
		lock (_gate)
		{
			status = _entries.Find(entry => entry.Status.Subscription.Id == subscriptionId)?.Status;
		}

		if (status is null)
		{
			return;
		}

		if (status.State is MqttSubscriptionState.Active or MqttSubscriptionState.Subscribing)
		{
			try
			{
				await _client.UnsubscribeAsync(
					new MqttClientUnsubscribeOptionsBuilder().WithTopicFilter(status.Subscription.TopicFilter).Build(),
					cancellationToken);
			}
			catch (Exception ex) when (ex is MqttCommunicationException or MqttProtocolViolationException or MqttClientNotConnectedException or OperationCanceledException or InvalidOperationException)
			{
				// The row goes either way: a connection that cannot unsubscribe is already on its way out.
				_logger.LogDebug("Unsubscribing failed: {Error}", LogSafe.Describe(ex));
			}
		}

		lock (_gate)
		{
			_entries.RemoveAll(entry => entry.Status.Subscription.Id == subscriptionId);
		}

		Changed?.Invoke();
	}

	/// <summary>
	/// Counts a received message against every subscription whose filter matches its topic. Nothing is raised: the
	/// view redraws from the store's version, so the counters ride along with the next redraw instead of turning
	/// every message into an event.
	/// </summary>
	public void CountMatch(string topic)
	{
		lock (_gate)
		{
			foreach (Entry entry in _entries)
			{
				if (entry.Status.State == MqttSubscriptionState.Active && MqttTopicFilters.Matches(topic, entry.Status.Subscription.TopicFilter))
				{
					entry.Status = entry.Status with { Matched = entry.Status.Matched + 1 };
				}
			}
		}
	}

	/// <summary>Marks every row as no longer subscribed, for a connection that has gone.</summary>
	public void MarkClosed()
	{
		lock (_gate)
		{
			foreach (Entry entry in _entries)
			{
				if (entry.Status.State is MqttSubscriptionState.Active or MqttSubscriptionState.Subscribing)
				{
					entry.Status = entry.Status with { State = MqttSubscriptionState.Idle, GrantedQos = null };
				}
			}
		}

		Changed?.Invoke();
	}

	private static string Describe(MqttClientSubscribeResultCode? code) => code switch
	{
		null => "The broker answered the subscribe with nothing.",
		MqttClientSubscribeResultCode.NotAuthorized => "The broker does not allow this client to subscribe to that filter.",
		MqttClientSubscribeResultCode.TopicFilterInvalid => "The broker rejected the filter as invalid.",
		MqttClientSubscribeResultCode.QuotaExceeded => "The broker is at its subscription limit for this client.",
		MqttClientSubscribeResultCode.WildcardSubscriptionsNotSupported => "The broker does not support wildcards in a filter.",
		MqttClientSubscribeResultCode.SharedSubscriptionsNotSupported => "The broker does not support shared subscriptions.",
		_ => $"The broker refused the subscription ({code}).",
	};

	private MqttSubscriptionStatus Update(MqttSubscription subscription, bool sessionOnly, Func<MqttSubscriptionStatus, MqttSubscriptionStatus> update)
	{
		MqttSubscriptionStatus result;
		lock (_gate)
		{
			Entry? entry = _entries.Find(existing => existing.Status.Subscription.Id == subscription.Id);
			if (entry is null)
			{
				entry = new Entry(new MqttSubscriptionStatus
				{
					Subscription = subscription,
					State = MqttSubscriptionState.Idle,
					IsSessionOnly = sessionOnly,
				});
				_entries.Add(entry);
			}

			// The filter and QoS may have been edited between attempts, so the row takes the new ones.
			entry.Status = update(entry.Status with { Subscription = subscription });
			result = entry.Status;
		}

		Changed?.Invoke();
		return result;
	}

	private sealed class Entry
	{
		public Entry(MqttSubscriptionStatus status) => Status = status;

		public MqttSubscriptionStatus Status { get; set; }
	}
}
