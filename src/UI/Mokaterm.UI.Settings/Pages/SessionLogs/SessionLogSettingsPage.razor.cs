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
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.UI.Settings.Pages.SessionLogs;

/// <summary>
/// Settings page for <see cref="SessionLogSettings"/>, with the files already written, a way to save one and ways to
/// delete them.
/// </summary>
public sealed partial class SessionLogSettingsPage : SettingsSectionBase<SessionLogSettings>
{
	private const string PageTitle = "Session logs";

	private const string FolderDescription =
		"Where the files go. Leave it empty to keep them under the app's data folder, next to the crash logs.";

	private static readonly IReadOnlyList<EnumOption<SessionLogFormat>> FormatOptions =
	[
		new(SessionLogFormat.PlainText, "Plain text"),
		new(SessionLogFormat.Raw, "Raw"),
		new(SessionLogFormat.Asciicast, "Timed"),
	];

	private IReadOnlyList<SessionLogFile> _files = [];
	private HashSet<SessionLogFile> _selected = [];
	private SessionLogUsage _usage = SessionLogUsage.Empty;
	private SessionLogFile? _playing;
	private bool _playerOpen;
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

	private string FilesDescription => _usage.Files switch
	{
		0 => string.Create(CultureInfo.CurrentCulture, $"In {Recorder.Folder}."),
		1 => string.Create(CultureInfo.CurrentCulture, $"In {Recorder.Folder}. One file takes {DisplayFormat.Bytes(_usage.Bytes)}."),
		_ when _usage.Files > _files.Count => string.Create(
			CultureInfo.CurrentCulture,
			$"In {Recorder.Folder}. {_usage.Files} files take {DisplayFormat.Bytes(_usage.Bytes)}; the newest {_files.Count} are listed."),
		_ => string.Create(CultureInfo.CurrentCulture, $"In {Recorder.Folder}. {_usage.Files} files take {DisplayFormat.Bytes(_usage.Bytes)}."),
	};

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

	private static string FileCount(int count) =>
		count == 1 ? "1 log file" : string.Create(CultureInfo.CurrentCulture, $"{count} log files");

	private void Reload()
	{
		_files = Recorder.ListFiles();
		_usage = Recorder.Usage();

		// A selected row that went (deleted, or moved past the listing) must not stay behind in the next delete.
		HashSet<SessionLogFile> listed = [.. _files];
		_selected = [.. _selected.Where(listed.Contains)];
	}

	private void Play(SessionLogFile file)
	{
		_playing = file;
		_playerOpen = true;
	}

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

	private async Task DeleteFilesAsync(IReadOnlyCollection<SessionLogFile> files)
	{
		if (_busy || files.Count == 0)
		{
			return;
		}

		string message = files.Count == 1
			? $"Delete {files.First().Name}? It cannot be brought back."
			: string.Create(CultureInfo.CurrentCulture, $"Delete {FileCount(files.Count)}? They cannot be brought back.");
		await RunDeleteAsync(
			new ConfirmPrompt { Title = "Delete session logs", Message = message, ConfirmText = "Delete", Destructive = true },
			cancellationToken => Recorder.DeleteAsync([.. files.Select(file => file.Name)], cancellationToken));
	}

	private Task DeleteAllAsync()
	{
		string message = string.Create(
			CultureInfo.CurrentCulture,
			$"Delete every session log in {Recorder.Folder}? That is {FileCount(_usage.Files)}, {DisplayFormat.Bytes(_usage.Bytes)}, and they cannot be brought back. A file a session is still writing is kept, and so is anything in the folder that is not a session log.");
		return RunDeleteAsync(
			new ConfirmPrompt { Title = "Delete all session logs", Message = message, ConfirmText = "Delete all", Destructive = true },
			Recorder.DeleteAllAsync);
	}

	private async Task RunDeleteAsync(ConfirmPrompt prompt, Func<CancellationToken, Task<SessionLogDeletion>> delete)
	{
		if (_busy || !(await Interaction.ConfirmAsync(prompt)).Confirmed)
		{
			return;
		}

		_busy = true;
		try
		{
			Report(await delete(CancellationToken.None));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The message carries the folder, so only the kind of failure is logged.
			Logger.LogWarning("Deleting session logs failed: {Failure}", LogSafe.Describe(ex));
			Interaction.Notify(NoticeSeverity.Error, "The log files could not be deleted.");
		}
		finally
		{
			_busy = false;
			ReloadAndRender();
		}
	}

	private void Report(SessionLogDeletion deletion)
	{
		List<string> parts = [];
		if (deletion.Deleted > 0)
		{
			parts.Add(string.Create(CultureInfo.CurrentCulture, $"Deleted {FileCount(deletion.Deleted)}."));
		}

		if (deletion.InUse > 0)
		{
			parts.Add(deletion.InUse == 1
				? "One is still being written and was kept."
				: string.Create(CultureInfo.CurrentCulture, $"{deletion.InUse} are still being written and were kept."));
		}

		if (deletion.Failed > 0)
		{
			parts.Add(deletion.Failed == 1
				? "One could not be deleted."
				: string.Create(CultureInfo.CurrentCulture, $"{deletion.Failed} could not be deleted."));
		}

		if (parts.Count == 0)
		{
			parts.Add("Nothing was deleted: the files were already gone.");
		}

		Interaction.Notify(deletion.Failed > 0 ? NoticeSeverity.Warning : NoticeSeverity.Success, string.Join(" ", parts));
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
