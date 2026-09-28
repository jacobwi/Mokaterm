using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.UI.Settings.Pages.Updates;

/// <summary>
/// Settings page for <see cref="UpdateSettings"/> and the update actions themselves. The host that can replace its own
/// files registers this page next to its <see cref="IAppUpdater"/>, so the page also reads without one.
/// </summary>
public sealed partial class UpdateSettingsPage : SettingsSectionBase<UpdateSettings>
{
	private const string PageTitle = "Updates";

	private IAppUpdater? _updater;
	private bool _busy;

	[Inject]
	private IServiceProvider Services { get; set; } = default!;

	[Inject]
	private IAppEnvironment AppEnvironment { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	private AppUpdateState? State => _updater?.State;

	private bool Busy => _busy || State?.Stage is UpdateStage.Checking or UpdateStage.Downloading;

	/// <summary>A check is pointless with no feed, and impossible for a copy that cannot replace its own files.</summary>
	private bool CanCheck => !Busy && State?.Stage is not (null or UpdateStage.NotConfigured or UpdateStage.NotInstalled);

	private string StatusLabel => State?.Stage switch
	{
		UpdateStage.NotConfigured => "Off",
		UpdateStage.NotInstalled => "Not installed",
		UpdateStage.Checking => "Checking",
		UpdateStage.UpToDate => "Up to date",
		UpdateStage.Available => "Update available",
		UpdateStage.Downloading => "Downloading",
		UpdateStage.ReadyToRestart => "Ready to restart",
		UpdateStage.Failed => "Failed",
		UpdateStage.Unknown => "Not checked",
		_ => "Unavailable",
	};

	private MokaColor StatusColor => State?.Stage switch
	{
		UpdateStage.UpToDate => MokaColor.Success,
		UpdateStage.Available or UpdateStage.ReadyToRestart => MokaColor.Warning,
		UpdateStage.Failed => MokaColor.Error,
		_ => MokaColor.Info,
	};

	private string StatusDescription => State switch
	{
		null => "This build has no updater, so it is replaced the way it was installed.",
		{ Message: { Length: > 0 } message } => message,
		{ Stage: UpdateStage.UpToDate, LastCheck: { } at } => $"Nothing newer in the feed. Checked {DisplayFormat.Timestamp(at)}.",
		{ Stage: UpdateStage.UpToDate } => "Nothing newer in the feed.",
		{ Stage: UpdateStage.Checking } => "Reading the feed.",
		{ Stage: UpdateStage.Available } => "A newer version is in the feed. Downloading it changes nothing until you restart.",
		{ Stage: UpdateStage.Downloading } => "Fetching the new version.",
		{ Stage: UpdateStage.ReadyToRestart } => "The new version is on disk and replaces this one on the next start.",
		{ Stage: UpdateStage.NotConfigured } => "No feed is set, so nothing is checked.",
		{ Stage: UpdateStage.NotInstalled } => "This copy was not installed, so it cannot replace its own files.",
		{ Stage: UpdateStage.Failed } => "The last attempt failed.",
		_ => "Nothing has been checked in this run.",
	};

	protected override void OnInitialized()
	{
		base.OnInitialized();

		// A host that cannot replace its own files registers no updater; the page then shows the settings and says why.
		_updater = Services.GetService<IAppUpdater>();
		if (_updater is not null)
		{
			_updater.StateChanged += OnUpdateStateChanged;
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && _updater is not null)
		{
			_updater.StateChanged -= OnUpdateStateChanged;
		}

		base.Dispose(disposing);
	}

	private static string? ValidateFeed(string value) =>
		UpdateSettings.NormalizeFeed(value).Length == 0 ? UpdateSettings.FeedRequirement : null;

	private void OnUpdateStateChanged(AppUpdateState state) => _ = InvokeAsync(StateHasChanged);

	private Task SetFeedAsync(string value) => UpdateAsync(s => s with { Feed = value });

	private async Task CheckAsync()
	{
		if (_updater is null)
		{
			return;
		}

		_busy = true;
		try
		{
			await _updater.CheckAsync();
		}
		finally
		{
			_busy = false;
		}
	}

	private async Task DownloadAsync()
	{
		if (_updater is null)
		{
			return;
		}

		_busy = true;
		try
		{
			await _updater.DownloadAsync();
		}
		finally
		{
			_busy = false;
		}
	}

	private async Task RestartAsync()
	{
		if (_updater is null)
		{
			return;
		}

		bool confirmed = (await Interaction.ConfirmAsync(new ConfirmPrompt
		{
			Title = "Restart and update",
			Message = "Mokaterm closes every open session, installs the new version and starts again. Work in a terminal that is not saved is lost.",
			ConfirmText = "Restart",
		})).Confirmed;

		if (!confirmed)
		{
			return;
		}

		_busy = true;
		try
		{
			// A restart that works never comes back here, so the page only has to cope with one that does not.
			await _updater.ApplyAndRestartAsync();
		}
		finally
		{
			_busy = false;
		}
	}
}
