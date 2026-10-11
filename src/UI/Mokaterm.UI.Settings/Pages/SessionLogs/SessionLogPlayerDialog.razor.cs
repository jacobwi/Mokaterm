using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Terminal.Playback;

namespace Mokaterm.UI.Settings.Pages.SessionLogs;

/// <summary>
/// Plays one session log: an asciicast with its timing, a plain or raw log all at once. The file is read only while
/// the dialog is open, and refused with a message past <see cref="TerminalRecordingReader.MaxFileBytes"/> instead of
/// being loaded whole.
/// </summary>
public sealed partial class SessionLogPlayerDialog : DraftDialogBase, IDisposable
{
	private SessionLogFile? _file;
	private TerminalRecording? _recording;
	private TerminalSettings _terminalSettings = new();
	private string? _error;
	private CancellationTokenSource? _loading;

	[Inject]
	private ISessionLogRecorder Recorder { get; set; } = default!;

	[Inject]
	private ISettingsService SettingsService { get; set; } = default!;

	[Inject]
	private ILogger<SessionLogPlayerDialog> Logger { get; set; } = default!;

	/// <summary>The file to play, one the recorder listed.</summary>
	[Parameter]
	public SessionLogFile? File { get; set; }

	/// <summary>What the player could not show, said once above it.</summary>
	private string? Notes
	{
		get
		{
			if (_recording is not { } recording)
			{
				return null;
			}

			List<string> notes = [];
			if (!recording.IsTimed)
			{
				notes.Add("This log has no times, so it is shown whole.");
			}

			if (recording.SkippedLines > 0)
			{
				notes.Add(recording.SkippedLines == 1
					? "One line of the file is not something the player understands and was left out."
					: string.Create(CultureInfo.CurrentCulture, $"{recording.SkippedLines} lines of the file are not something the player understands and were left out."));
			}

			if (recording.IsTruncated)
			{
				notes.Add("The file holds more events than the player keeps, so its end is missing.");
			}

			return notes.Count == 0 ? null : string.Join(" ", notes);
		}
	}

	protected override void Load()
	{
		_file = File;
		_terminalSettings = SettingsService.Get<TerminalSettings>();
		if (_file is { } file)
		{
			CancellationTokenSource loading = new();
			_loading = loading;
			_ = LoadAsync(file, loading.Token);
		}
	}

	// A log holds whatever the server printed, so a closed dialog keeps none of it.
	protected override void Clear()
	{
		if (_loading is { } loading)
		{
			_loading = null;
			loading.Cancel();
			loading.Dispose();
		}

		_file = null;
		_recording = null;
		_error = null;
	}

	public void Dispose()
	{
		Clear();
		GC.SuppressFinalize(this);
	}

	private async Task LoadAsync(SessionLogFile file, CancellationToken cancellationToken)
	{
		TerminalRecordingResult result;
		try
		{
			if (file.Length > TerminalRecordingReader.MaxFileBytes)
			{
				result = TerminalRecordingResult.Failed(TerminalRecordingReader.TooLarge(file.Length));
			}
			else
			{
				await using Stream stream = Recorder.OpenRead(file.Name);
				result = await TerminalRecordingReader.ReadAsync(stream, file.Name, cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
		{
			// The message carries the folder, so only the kind of failure is logged.
			Logger.LogWarning("Reading a session log for playback failed: {Failure}", LogSafe.Describe(ex));
			result = TerminalRecordingResult.Failed("The log file could not be read. It may have been moved or deleted.");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// Nothing awaits this load, so whatever it meets has to end up in the dialog rather than nowhere.
			Logger.LogError("Playing a session log failed: {Failure}", LogSafe.Describe(ex));
			result = TerminalRecordingResult.Failed("The log could not be played.");
		}

		if (cancellationToken.IsCancellationRequested)
		{
			return;
		}

		await InvokeAsync(() =>
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}

			_recording = result.Recording;
			_error = result.Error;
			StateHasChanged();
		});
	}
}
