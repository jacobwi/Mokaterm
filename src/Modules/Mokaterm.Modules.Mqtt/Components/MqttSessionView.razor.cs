using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Mqtt.Components;

/// <summary>
/// The MQTT explorer: a topic tree on the left, the selected topic's messages and payload on the right, with
/// publishing and subscriptions in dialogs from the toolbar.
/// </summary>
/// <remarks>
/// The view redraws on a timer instead of per message. A broker can publish thousands a second and each one would
/// otherwise be a render; the store carries a version, and a tick that finds it unchanged does nothing at all.
/// </remarks>
public sealed partial class MqttSessionView : SessionViewBase, IDisposable
{
	/// <summary>How often the view looks for new messages. Fast enough to feel live, slow enough to stay cheap.</summary>
	private const int RedrawMilliseconds = 250;

	/// <summary>Rows the topic tree draws. Every one is a real element, so this is what keeps the page small.</summary>
	private const int MaxTopicRows = 600;

	/// <summary>Messages one topic shows. The store may keep more.</summary>
	private const int MaxMessageRows = 200;

	private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
	private List<MqttTopicRow> _rows = [];
	private List<MqttMessage> _messages = [];
	private IReadOnlyList<MqttSubscriptionStatus> _subscriptions = [];
	private ISessionHandle? _handle;
	private IProtocolSession? _attachedSession;
	private IMqttConnection? _connection;
	private MqttMessageStore? _store;
	private MqttTopicRow? _selectedRow;
	private MqttMessage? _selectedMessage;
	private ITimer? _timer;
	private string? _selectedTopic;
	private string _search = "";
	private long _seenVersion = -1;
	private bool _publishOpen;
	private bool _subscriptionsOpen;
	private bool _disposed;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	private MqttQos DefaultQos => Settings.Get<MqttSettings>().Clamped().DefaultQualityOfService;

	private string IdleDescription => SessionIdleText.For(Session.State, "Reconnect the session to open the broker again.");

	private string CountsText => _store is { } store
		? string.Create(CultureInfo.CurrentCulture, $"{store.Received} msg / {store.TopicCount} topics")
		: "";

	private string CountsTitle => _store is { } store
		? string.Create(
			CultureInfo.CurrentCulture,
			$"{store.Received} messages received, {store.Kept} kept ({DisplayFormat.Bytes(store.KeptBytes)}), {store.Dropped} dropped by the limits in Settings, MQTT")
		: "";

	private string SubscriptionsText => string.Create(
		CultureInfo.CurrentCulture,
		$"{_subscriptions.Count(status => status.State == MqttSubscriptionState.Active)}/{_subscriptions.Count}");

	private string SubscriptionsTitle => string.Create(
		CultureInfo.CurrentCulture,
		$"Subscriptions: {_subscriptions.Count(status => status.State == MqttSubscriptionState.Active)} of {_subscriptions.Count} active");

	private string ClientTitle => _connection is { } connection
		? string.Create(CultureInfo.CurrentCulture, $"{connection.Info.Endpoint} as {connection.Info.ClientId}")
		: "";

	private string TransportTitle => _connection is { } connection
		? string.Create(
			CultureInfo.CurrentCulture,
			$"MQTT {Version(connection.Info.Protocol)} over {(connection.Info.Transport == MqttTransport.WebSocket ? "WebSockets" : "TCP")}")
		: "";

	private string TopicsFullText => _store is { } store
		? string.Create(
			CultureInfo.CurrentCulture,
			$"The topic list is full at {store.TopicCount}. Messages on topics it has not seen are counted and dropped; raise the topics per session in Settings, MQTT, or clear what is kept.")
		: "";

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_timer?.Dispose();
		_timer = null;
		if (_handle is not null)
		{
			_handle.Changed -= OnSessionChanged;
			_handle = null;
		}

		Detach();
	}

	protected override void OnParametersSet()
	{
		if (!ReferenceEquals(_handle, Session))
		{
			if (_handle is not null)
			{
				_handle.Changed -= OnSessionChanged;
			}

			_handle = Session;
			_handle.Changed += OnSessionChanged;
		}

		if (!ReferenceEquals(_attachedSession, Session.Session))
		{
			SwitchSession();
		}
	}

	protected override void OnAfterRender(bool firstRender)
	{
		if (firstRender && !_disposed)
		{
			TimeSpan period = TimeSpan.FromMilliseconds(RedrawMilliseconds);
			_timer = Time.CreateTimer(_ => Tick(), state: null, period, period);
		}
	}

	private static string Version(MqttProtocolLevel level) => level switch
	{
		MqttProtocolLevel.V311 => "3.1.1",
		MqttProtocolLevel.V310 => "3.1",
		_ => "5.0",
	};

	/// <summary>
	/// A tick redraws only when the store moved. The comparison is a read of one long, so a quiet session costs four
	/// of those a second and nothing else. State and subscriptions come through <c>IMqttConnection.Changed</c>
	/// instead, because those change rarely enough to redraw on.
	/// </summary>
	private void Tick()
	{
		if (_disposed || _store is null)
		{
			return;
		}

		if (_store.Version == _seenVersion)
		{
			return;
		}

		_ = InvokeAsync(() =>
		{
			if (!_disposed)
			{
				Refresh();
				StateHasChanged();
			}
		});
	}

	private void SwitchSession()
	{
		Detach();
		_attachedSession = Session.Session;
		_connection = _attachedSession?.GetFeature<IMqttConnection>();
		_store = _connection?.Messages;
		_selectedTopic = null;
		_selectedRow = null;
		_selectedMessage = null;
		_rows = [];
		_messages = [];
		_subscriptions = [];
		_expanded.Clear();
		_seenVersion = -1;
		if (_connection is not null)
		{
			_connection.Changed += OnConnectionChanged;
			Refresh();
		}
	}

	private void Detach()
	{
		if (_connection is not null)
		{
			_connection.Changed -= OnConnectionChanged;
			_connection = null;
		}

		_store = null;
	}

	/// <summary>Rebuilds everything the page draws, under the store's own lock, once per redraw.</summary>
	private void Refresh()
	{
		if (_connection is not { } connection || _store is not { } store)
		{
			return;
		}

		_seenVersion = store.Version;
		_subscriptions = connection.Subscriptions;
		_rows = store.Rows(_expanded.Contains, _search, Settings.Get<MqttSettings>().SortTopicsByTime, MaxTopicRows);
		_selectedRow = store.Row(_selectedTopic);
		_messages = store.Messages(_selectedTopic, MaxMessageRows);

		// The selected message may have aged out of the store, in which case the newest one takes its place.
		if (_selectedMessage is { } selected && !_messages.Exists(message => message.Sequence == selected.Sequence))
		{
			_selectedMessage = _messages.Count > 0 ? _messages[0] : null;
		}
	}

	private void OnSessionChanged() => _ = InvokeAsync(StateHasChanged);

	private void OnConnectionChanged() => _ = InvokeAsync(() =>
	{
		if (!_disposed)
		{
			Refresh();
			StateHasChanged();
		}
	});

	private void OnTopicSelected(string path)
	{
		if (_selectedTopic == path)
		{
			return;
		}

		_selectedTopic = path;
		_selectedMessage = null;
		Refresh();

		// The newest message opens with the topic, which is what anyone wants to see first.
		_selectedMessage = _messages.Count > 0 ? _messages[0] : null;
	}

	private void OnToggleTopic(string path)
	{
		if (!_expanded.Remove(path))
		{
			_expanded.Add(path);
		}

		Refresh();
	}

	private void OnSearchChanged(string value)
	{
		_search = value ?? "";
		Refresh();
	}

	private void OnMessageSelected(MqttMessage message) => _selectedMessage = message;

	private void OpenPublish() => _publishOpen = true;

	private void OpenSubscriptions()
	{
		Refresh();
		_subscriptionsOpen = true;
	}

	private void OnPublishOpenChanged(bool open) => _publishOpen = open;

	private void OnSubscriptionsOpenChanged(bool open) => _subscriptionsOpen = open;

	private void ClearKept()
	{
		_store?.Clear();
		_selectedTopic = null;
		_selectedRow = null;
		_selectedMessage = null;
		_expanded.Clear();
		Refresh();
	}
}
