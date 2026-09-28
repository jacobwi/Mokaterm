using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.Maui.Services;

namespace Mokaterm.Maui.Components;

/// <summary>The desktop page's root component: renders the shared shell and registers its sessions for shutdown.</summary>
public sealed partial class DesktopRoot : ComponentBase, IDisposable
{
	private IDisposable? _registration;

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
	private ILogger<DesktopRoot> Logger { get; set; } = default!;

	public void Dispose() => _registration?.Dispose();

	protected override void OnInitialized() => _registration = Registry.Track(Sessions, Transfers);

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender)
		{
			await CheckForUpdatesAsync();
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
