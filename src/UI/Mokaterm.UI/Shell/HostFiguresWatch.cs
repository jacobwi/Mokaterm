using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.Shell;

/// <summary>
/// The one lease the status bar holds on a session's live figures: on the session it is pointed at, while that session
/// is connected and the figures are wanted. Pointing it elsewhere moves the lease, a drop lets it go, and a reconnect,
/// which brings a new live session with a new feature, takes a new one.
/// </summary>
/// <remarks>Call <see cref="Update"/> from the renderer only. <see cref="Changed"/> fires on the feature's own thread.</remarks>
internal sealed class HostFiguresWatch : IDisposable
{
	private IHostMonitorFeature? _feature;
	private IDisposable? _lease;
	private TimeSpan _interval;

	/// <summary>Raised when the watched feature's figures change. It may fire on any thread.</summary>
	public event Action? Changed;

	/// <summary>The feature watched now, or null when nothing is.</summary>
	public IHostMonitorFeature? Feature => _feature;

	/// <summary>
	/// Watches <paramref name="session"/>'s figures every <paramref name="interval"/> when <paramref name="wanted"/>
	/// and the session is connected with a live session that offers them; lets go of anything else.
	/// </summary>
	/// <param name="wanted">Whether anyone may look: the setting is on, the page is seen and the vault is open.</param>
	public void Update(ISessionHandle? session, bool wanted, TimeSpan interval)
	{
		IHostMonitorFeature? feature = wanted && session is { State: SessionState.Connected, Session: { } live }
			? live.GetFeature<IHostMonitorFeature>()
			: null;
		if (ReferenceEquals(feature, _feature) && (feature is null || interval == _interval))
		{
			return;
		}

		Release();
		if (feature is null)
		{
			return;
		}

		_feature = feature;
		_interval = interval;
		feature.Changed += OnFeatureChanged;
		_lease = feature.Watch(interval);
	}

	public void Dispose() => Release();

	private void Release()
	{
		_lease?.Dispose();
		_lease = null;
		if (_feature is { } feature)
		{
			feature.Changed -= OnFeatureChanged;
			_feature = null;
		}
	}

	private void OnFeatureChanged() => Changed?.Invoke();
}
