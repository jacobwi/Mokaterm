using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Shell;

/// <summary>
/// The status bar's live figures of the active session's machine: CPU, memory and root disk use, which a click opens in
/// the session's tool for them. The shell knows nothing of the protocol: a session that offers
/// <see cref="Abstractions.ServerAdmin.IHostMonitorFeature"/> gets the item.
/// </summary>
/// <remarks>
/// It reads only while someone can look: the setting is on, the session is connected, the page is not hidden and the
/// vault is open. The status bar hands it the active session of the focused pane, so switching tabs moves the lease.
/// </remarks>
public sealed partial class HostFiguresItem : ComponentBase, IDisposable
{
	private readonly HostFiguresWatch _watch = new();
	private ISessionHandle? _session;

	/// <summary>The session whose machine to show, or null for none.</summary>
	[Parameter]
	public ISessionHandle? Session { get; set; }

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private PageVisibility Visibility { get; set; } = default!;

	[Inject]
	private IVault Vault { get; set; } = default!;

	[Inject]
	private SessionToolActions Tools { get; set; } = default!;

	/// <summary>The session tool the figures name, when the session's protocol has it.</summary>
	private SessionToolDescriptor? Tool => _session is { } session && _watch.Feature?.ToolKey is { Length: > 0 } key
		? Tools.Find(session, key)
		: null;

	public void Dispose()
	{
		_watch.Changed -= OnFiguresChanged;
		Settings.Changed -= OnSettingsChanged;
		Visibility.Changed -= OnConditionsChanged;
		Vault.StatusChanged -= OnVaultStatusChanged;
		Follow(null);
		_watch.Dispose();
	}

	protected override void OnInitialized()
	{
		_watch.Changed += OnFiguresChanged;
		Settings.Changed += OnSettingsChanged;
		Visibility.Changed += OnConditionsChanged;
		Vault.StatusChanged += OnVaultStatusChanged;
	}

	protected override void OnParametersSet()
	{
		Follow(Session);
		Refresh();
	}

	private static string LevelClass(UsageLevel level) => level switch
	{
		UsageLevel.Full => "mt-host-figure mt-host-figure--full",
		UsageLevel.Warn => "mt-host-figure mt-host-figure--warn",
		_ => "mt-host-figure",
	};

	private EventCallback<MouseEventArgs> ClickFor(SessionToolDescriptor? tool) =>
		tool is null || _session is not { } session
			? default
			: EventCallback.Factory.Create<MouseEventArgs>(this, () => Tools.OpenAsync(session, tool));

	/// <summary>
	/// Follows the session itself, because its state and its live session change without the status bar drawing again:
	/// a drop lets the lease go and a reconnect, which brings a new live session, takes a new one.
	/// </summary>
	private void Follow(ISessionHandle? session)
	{
		if (ReferenceEquals(session, _session))
		{
			return;
		}

		if (_session is { } previous)
		{
			previous.Changed -= OnConditionsChanged;
		}

		_session = session;
		if (session is not null)
		{
			session.Changed += OnConditionsChanged;
		}
	}

	private void Refresh()
	{
		AppearanceSettings appearance = Settings.Get<AppearanceSettings>().Clamped();
		bool wanted = appearance.ShowHostFigures && Visibility.IsVisible && Vault.Status == VaultStatus.Unlocked;
		_watch.Update(_session, wanted, TimeSpan.FromSeconds(appearance.HostFiguresSeconds));
	}

	// Every one of these may fire on any thread.
	private void OnConditionsChanged() => _ = InvokeAsync(() =>
	{
		Refresh();
		StateHasChanged();
	});

	private void OnVaultStatusChanged(VaultStatus _) => OnConditionsChanged();

	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey == AppearanceSettings.SectionKey)
		{
			OnConditionsChanged();
		}
	}

	private void OnFiguresChanged() => _ = InvokeAsync(StateHasChanged);
}
