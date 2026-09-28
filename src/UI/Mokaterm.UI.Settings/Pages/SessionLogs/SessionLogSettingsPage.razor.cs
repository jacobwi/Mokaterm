using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Settings.Pages.SessionLogs;

/// <summary>Settings page for <see cref="SessionLogSettings"/>, with the files already written and a way to save one.</summary>
public sealed partial class SessionLogSettingsPage : SettingsSectionBase<SessionLogSettings>
{
	private const string PageTitle = "Session logs";

	private const string FolderDescription =
		"Where the files go. Leave it empty to keep them under the app's data folder, next to the crash logs.";

	private static readonly IReadOnlyList<EnumOption<SessionLogFormat>> FormatOptions =
	[
		new(SessionLogFormat.PlainText, "Plain text"),
		new(SessionLogFormat.Raw, "Raw"),
	];

	private IReadOnlyList<SessionLogFile> _files = [];
	private bool _busy;

	[Inject]
	private ISessionLogRecorder Recorder { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private IServiceProvider Services { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	[Inject]
	private ILogger<SessionLogSettingsPage> Logger { get; set; } = default!;

	/// <summary>Saving needs a host that can write a local file: a picker on the desktop, a download in the browser.</summary>
	private bool CanSaveFiles => Services.GetService<ILocalFileAccess>() is not null;

	private string FilesDescription => string.Create(
		CultureInfo.CurrentCulture,
		$"In {Recorder.Folder}. The newest ones are listed, and nothing removes them, so delete what you are done with yourself.");

	protected override void OnInitialized()
	{
		base.OnInitialized();
		Recorder.Changed += OnRecordingChanged;
		SettingsService.Changed += OnSectionChanged;
		Reload();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			Recorder.Changed -= OnRecordingChanged;
			SettingsService.Changed -= OnSectionChanged;
		}

		base.Dispose(disposing);
	}

	private static string? ValidateFolder(string value) =>
		SessionLogSettings.NormalizeFolder(value).Length > 0 ? null : SessionLogSettings.FolderRequirement;

	private void Reload() => _files = Recorder.ListFiles();

	private Task SetFolderAsync(string value) =>
		UpdateAsync(settings => settings with { Folder = SessionLogSettings.NormalizeFolder(value) });

	private void OnRecordingChanged() => _ = InvokeAsync(ReloadAndRender);

	private void OnSectionChanged(string sectionKey)
	{
		// The folder may have moved, so the list is read again rather than left showing the old one's files.
		if (sectionKey == SessionLogSettings.SectionKey)
		{
			_ = InvokeAsync(ReloadAndRender);
		}
	}

	private void ReloadAndRender()
	{
		Reload();
		StateHasChanged();
	}

	private async Task SaveAsync(SessionLogFile file)
	{
		if (_busy || Services.GetService<ILocalFileAccess>() is not { } files)
		{
			return;
		}

		_busy = true;
		try
		{
			bool saved = await files.SaveFileAsync(
				file.Name,
				file.Length,
				async (stream, cancellationToken) =>
				{
					// Opened here rather than up front: the file may be longer by now, and the session writing it keeps it open.
					await using Stream source = Recorder.OpenRead(file.Name);
					await source.CopyToAsync(stream, cancellationToken);
				});

			if (saved)
			{
				Interaction.Notify(NoticeSeverity.Success, string.Create(CultureInfo.CurrentCulture, $"{file.Name} was saved."));
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The message carries the path this runs against, so only the kind of failure is logged.
			Logger.LogWarning("Saving a session log failed: {Failure}", LogSafe.Describe(ex));
			Interaction.Notify(NoticeSeverity.Error, "The log file could not be saved.");
		}
		finally
		{
			_busy = false;
			ReloadAndRender();
		}
	}
}
