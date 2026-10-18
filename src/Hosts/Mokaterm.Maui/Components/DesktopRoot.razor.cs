using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.Core.Plugins;
using Mokaterm.Maui.Services;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Maui.Components;

/// <summary>The desktop page's root component: renders the shared shell and registers its sessions for shutdown.</summary>
public sealed partial class DesktopRoot : ComponentBase, IDisposable
{
	private IDisposable? _registration;
	private IDisposable? _notificationClicks;

	[Inject]
	private ISessionManager Sessions { get; set; } = default!;

	[Inject]
	private ITransferQueue Transfers { get; set; } = default!;

	[Inject]
	private DesktopSessions Registry { get; set; } = default!;

	[Inject]
	private IServiceProvider Services { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private IPendingClipboardClear PendingClipboardClear { get; set; } = default!;

	[Inject]
	private ISessionTabs SessionTabs { get; set; } = default!;

	[Inject]
	private NotificationClicks NotificationClicks { get; set; } = default!;

	[Inject]
	private ILogger<DesktopRoot> Logger { get; set; } = default!;

	public void Dispose()
	{
		_notificationClicks?.Dispose();
		_registration?.Dispose();
	}

	// The page's own state too: the tool windows that close with the main window report where they were just before, and
	// a secret copied just before is cleared rather than left on the clipboard when the process ends.
	protected override void OnInitialized()
	{
		_registration = Registry.Track(Sessions, Transfers, () => UI.Shell.ShellPersistence.FlushAsync(Services), PendingClipboardClear);
		_notificationClicks = NotificationClicks.Attach(click => InvokeAsync(() => ShowSession(click)));
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender)
		{
			// The window is up with its plugins loaded, so the next start may load them again (see PluginCatalog).
			Services.GetService<PluginCatalog>()?.ConfirmStarted();
			await CheckForUpdatesAsync();
		}
	}

	/// <summary>
	/// A click on a system notification about a session: its tab comes to the front, through the shell's own way of showing
	/// a session. The window is already in front by now.
	/// </summary>
	private void ShowSession(NotificationClick click)
	{
		if (click.SessionToShow(Sessions.Sessions) is { } sessionId)
		{
			SessionTabs.Show(sessionId);
		}
	}

	/// <summary>
	/// The startup update check: a notice and nothing more. Downloading and restarting stay in Settings, because a
	/// restart closes every live terminal.
	/// </summary>
	private async Task CheckForUpdatesAsync()
	{
		// Only a host that can replace its own files registers an updater, and only a set feed makes it check.
		if (Services.GetService<IAppUpdater>() is not { } updater)
		{
			return;
		}

		AppUpdateState? state;
		try
		{
			state = await updater.CheckAtStartupAsync();
		}
		catch (Exception ex)
		{
			// The window opens whatever the feed does, and a failed check is the updater's own business to report.
			Logger.LogWarning("The update check at startup failed: {Failure}", LogSafe.Describe(ex));
			return;
		}

		if (state is { Stage: UpdateStage.Available })
		{
			Interaction.Notify(
				NoticeSeverity.Info,
				$"Mokaterm {state.AvailableVersion} is out. Settings, then Updates, installs it.",
				"Update available");
		}
	}
}
