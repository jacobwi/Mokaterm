using System.Globalization;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Sessions.Zmodem;

namespace Mokaterm.Sessions.Terminal;

/// <summary>
/// Drives one terminal stream's ZMODEM transfers: watches the stream's output for an entry frame, hands protocol bytes to
/// a <see cref="ZmodemSession"/>, carries out that session's steps (channel writes, local file writes, uploads read from
/// picked files) and reports phase, offer and progress to the view. Keystrokes the user types while a transfer runs are
/// dropped: the channel belongs to the protocol until the transfer ends or is canceled.
/// </summary>
internal sealed class TerminalZmodem : ITerminalZmodem, IDisposable
{
	/// <summary>How often progress changes are reported while bytes flow.</summary>
	private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

	/// <summary>The window each speed estimate is taken over.</summary>
	private static readonly TimeSpan SpeedWindow = TimeSpan.FromMilliseconds(500);

	private readonly TerminalStream _stream;
	private readonly ILocalFileAccess? _localFiles;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly Lock _lock = new();

	// One session runs at a time; a finished one is replaced before the next chunk of output arrives.
	private ZmodemSession _session = new();

	// Everything that touches the session or the transfer state runs under this gate: the pump feeding output, the user
	// answering an offer, and the upload reader supplying bytes.
	private readonly SemaphoreSlim _engine = new(1, 1);

	// Only one flush of protocol bytes to the channel, and only one upload read loop, at a time.
	private readonly SemaphoreSlim _flushing = new(1, 1);
	private readonly SemaphoreSlim _uploading = new(1, 1);
	private readonly List<byte> _outbox = [];
	private readonly List<byte> _passthrough = [];
	private readonly CancellationTokenSource _lifetime = new();
	private Stream? _saveStream;
	private bool _accepting;
	private TaskCompletionSource<bool> _fileDone = NewCompletion();
	private Exception? _saveError;
	private List<LocalFileItem>? _uploadFiles;
	private Stream? _uploadStream;
	private int _uploadIndex = -1;
	private ZmodemPhase _phase;
	private ZmodemOutcome _outcome;
	private string? _result;
	private ZmodemFileOffer? _offer;
	private ZmodemProgress? _progress;
	private DateTimeOffset _progressAt;
	private DateTimeOffset _speedAt;
	private long _speedBytes;
	private double _bytesPerSecond;
	private int _completedFiles;
	private long _completedBytes;
	private bool _wasReceiving;

	public TerminalZmodem(TerminalStream stream, ILocalFileAccess? localFiles, TimeProvider timeProvider, ILogger logger)
	{
		_stream = stream;
		_localFiles = localFiles;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public event Action? Changed;

	public ZmodemPhase Phase => _phase;

	public ZmodemFileOffer? Offer => _offer;

	public ZmodemProgress? Progress => _progress;

	public ZmodemOutcome Outcome => _outcome;

	public string? Result => _result;

	/// <summary>True while the terminal's channel bytes belong to a transfer: keystrokes are dropped then.</summary>
	public bool Engaged => _phase is not (ZmodemPhase.Idle or ZmodemPhase.Done);

	/// <summary>
	/// Feeds channel output in. Returns how many bytes of it are terminal text rather than protocol; they are always a
	/// prefix of what was given.
	/// </summary>
	public async ValueTask<int> ConsumeOutputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		await _engine.WaitAsync(cancellationToken);
		try
		{
			_passthrough.Clear();
			ZmodemStep step = _session.Feed(data.Span);
			_passthrough.AddRange(step.Passthrough);
			await ApplyStepAsync(step, cancellationToken);
			return _passthrough.Count;
		}
		finally
		{
			_engine.Release();
		}
	}

	public async Task<bool> AcceptAsync(CancellationToken cancellationToken = default)
	{
		if (_phase != ZmodemPhase.Offering || _offer is not { } offer || _accepting)
		{
			return false;
		}

		if (_localFiles is null)
		{
			// A host with no way to save a file cannot take one; say so and let the user skip instead.
			await _stream.WriteLocalAsync(TerminalNotice.Format("this build of Mokaterm cannot save files, so the transfer was skipped."), cancellationToken);
			Skip();
			return false;
		}

		_accepting = true;
		bool saved;
		try
		{
			saved = await _localFiles.SaveFileAsync(
				SafeName(offer.FileName),
				offer.Length,
				ReceiveIntoAsync,
				cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "Saving the offered ZMODEM file failed.");
			await FailAsync("The file could not be saved here: " + ex.Message);
			return false;
		}
		finally
		{
			_accepting = false;
		}

		if (!saved)
		{
			// The save was declined — a canceled dialog or a closed page — so the file is skipped.
			Skip();
			return false;
		}

		if (_saveError is { } fault)
		{
			_saveError = null;
			await FailAsync("The file could not be saved here: " + fault.Message);
			return false;
		}

		return true;
	}

	public void Skip() => RunEngine(() =>
	{
		if (_session.State == ZmodemSessionState.Offering)
		{
			return ApplyStepAsync(_session.Skip(), CancellationToken.None);
		}

		return Task.CompletedTask;
	});

	public void Cancel() => RunEngine(async () =>
	{
		await ApplyStepAsync(_session.Cancel(), CancellationToken.None);
		CompleteFile(ok: false);
	});

	public void SendFiles(IReadOnlyList<LocalFileItem> files) => RunEngine(async () =>
	{
		if (_phase != ZmodemPhase.ChoosingFiles)
		{
			return;
		}

		List<ZmodemFileInfo> sendable = [];
		List<LocalFileItem> sources = [];
		foreach (LocalFileItem file in files)
		{
			// A stream of unknown length cannot be closed with a ZEOF, so it is not offered at all.
			if (!file.IsDirectory && file.Length is { } length && file.OpenReadAsync is not null && SafeName(file.Name).Length > 0)
			{
				sendable.Add(new ZmodemFileInfo(file.Name, length, file.LastModified, null));
				sources.Add(file);
			}
		}

		if (sources.Count == 0)
		{
			await ApplyStepAsync(_session.StartUpload([]), CancellationToken.None);
			Finish(ZmodemOutcome.Canceled, "No file was chosen, so nothing was sent.");
			RaiseChanged();
			return;
		}

		_uploadFiles = sources;
		_uploadIndex = -1;
		await ApplyStepAsync(_session.StartUpload(sendable), CancellationToken.None);
	});

	/// <summary>Ends any running transfer without protocol courtesies: the stream is going away.</summary>
	public void Abort()
	{
		RunEngine(async () =>
		{
			if (_session.Intercepts)
			{
				await ApplyStepAsync(_session.Cancel(), CancellationToken.None);
			}

			CompleteFile(ok: false);
		});
		_lifetime.Cancel();
	}

	/// <summary>Releases the transfer's own resources once the stream is gone. The session's answer for that is <see cref="Abort"/>.</summary>
	public void Dispose()
	{
		Abort();
		_uploading.Dispose();
		_flushing.Dispose();
		_engine.Dispose();
		_lifetime.Dispose();
	}

	/// <summary>Carries one engine step out: file writes, sends, phase moves and the end of a session.</summary>
	private async Task ApplyStepAsync(ZmodemStep step, CancellationToken cancellationToken)
	{
		foreach (byte[] chunk in step.FileData)
		{
			if (_saveStream is { } target)
			{
				try
				{
					await target.WriteAsync(chunk, cancellationToken);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					// The disk said no; remember it for the save in flight and stop the session.
					_saveError = ex;
					await ApplyStepAsync(_session.Cancel(), CancellationToken.None);
					return;
				}
			}

			TrackProgress();
		}

		if (step.Offer is { } offer)
		{
			_offer = new ZmodemFileOffer { FileName = offer.Name, Length = offer.Length, Modified = offer.Modified };
			_phase = ZmodemPhase.Offering;
			_progress = null;
			RaiseChanged();
		}

		// The bar follows the session's own state for the phases no other step announces; Done yields to a new session.
		if (_phase is (ZmodemPhase.Idle or ZmodemPhase.Done) && _session.State == ZmodemSessionState.ChoosingFiles)
		{
			_phase = ZmodemPhase.ChoosingFiles;
			RaiseChanged();
		}

		if (step.FileStarted)
		{
			_wasReceiving = _session.State == ZmodemSessionState.Receiving;
			if (_session.State == ZmodemSessionState.Sending)
			{
				_phase = ZmodemPhase.Sending;
			}

			_speedAt = _timeProvider.GetUtcNow();
			_speedBytes = 0;
			_bytesPerSecond = 0;
			ReportProgress(force: true);
		}

		if (step.FileFinished)
		{
			_completedFiles++;
			_completedBytes += _session.FileBytes;
			CompleteFile(ok: true);
			_progress = null;
		}

		if (step.DataWanted)
		{
			_ = SupplyUploadAsync(step.DataPosition, step.DataWantedBytes);
		}

		if (step.Send.Count > 0)
		{
			QueueSend(step.Send);
		}

		if (step.Done)
		{
			CompleteFile(ok: step.FileFinished);
			if (step.Aborted)
			{
				Finish(step.FailedReason is null ? ZmodemOutcome.Canceled : ZmodemOutcome.Failed, step.FailedReason ?? "Canceled.");
			}
			else
			{
				string result = _wasReceiving
					? $"{DescribeCount(_completedFiles)} saved ({DescribeBytes(_completedBytes)})."
					: $"{DescribeCount(_completedFiles)} sent ({DescribeBytes(_completedBytes)}).";
				Finish(ZmodemOutcome.Completed, result);
			}

			// A finished session never watches again; a fresh one takes the next chunk of output.
			_session = new ZmodemSession();
			CloseUpload();
			RaiseChanged();
		}
	}

	/// <summary>The save a host opened: registers it, lets the session accept the offer, waits for the file to end.</summary>
	private async Task ReceiveIntoAsync(Stream stream, CancellationToken saveToken)
	{
		await _engine.WaitAsync(saveToken);
		TaskCompletionSource<bool> done = _fileDone;
		try
		{
			if (_session.State != ZmodemSessionState.Offering)
			{
				return;
			}

			_saveStream = stream;
			_phase = ZmodemPhase.Receiving;
			await ApplyStepAsync(_session.Accept(), saveToken);
		}
		finally
		{
			_engine.Release();
		}

		await done.Task.WaitAsync(saveToken);
		_saveStream = null;
	}

	/// <summary>Reads the wanted bytes of the file being sent and feeds them to the session until it stops asking.</summary>
	private async Task SupplyUploadAsync(long position, int wanted)
	{
		if (_uploadFiles is null || wanted <= 0)
		{
			return;
		}

		await _uploading.WaitAsync();
		try
		{
			Stream? source = await OpenUploadAtAsync(position);
			if (source is null)
			{
				await FailAsync("The file to send could not be opened here.");
				return;
			}

			byte[] buffer = new byte[wanted];
			while (true)
			{
				int read = 0;
				while (read < buffer.Length)
				{
					int piece = await source.ReadAsync(buffer.AsMemory(read), _lifetime.Token);
					if (piece <= 0)
					{
						break;
					}

					read += piece;
				}

				ZmodemStep step;
				await _engine.WaitAsync(CancellationToken.None);
				try
				{
					if (_session.State != ZmodemSessionState.Sending)
					{
						return;
					}

					step = _session.SupplyData(buffer.AsSpan(0, read));
					TrackProgress();
					await ApplyStepAsync(step, CancellationToken.None);
				}
				finally
				{
					_engine.Release();
				}

				if (!step.DataWanted)
				{
					return;
				}

				position = step.DataPosition;
				if (step.DataWantedBytes != buffer.Length)
				{
					buffer = new byte[step.DataWantedBytes];
				}

				// A retransmit asks for an earlier position, which only a seek — or a reopened stream — can go back to.
				source = await OpenUploadAtAsync(position, source);
				if (source is null)
				{
					await FailAsync("The file to send could not be read here.");
					return;
				}
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			await FailAsync("The file to send could not be read here: " + ex.Message);
		}
		finally
		{
			_uploading.Release();
		}
	}

	/// <summary>The stream of the file being sent, positioned at <paramref name="position"/>: seek it when it can, else reopen and skip.</summary>
	private async Task<Stream?> OpenUploadAtAsync(long position, Stream? current = null)
	{
		if (_session.CurrentOutbound?.Name is not { } wantName || _uploadFiles is null)
		{
			await DisposeQuietlyAsync(current);
			return null;
		}

		int index = _uploadFiles.FindIndex(file => file.Name == wantName);
		if (index < 0 || _uploadFiles[index].OpenReadAsync is not { } open)
		{
			await DisposeQuietlyAsync(current);
			return null;
		}

		if (current is not null && index == _uploadIndex && current.CanSeek)
		{
			current.Position = position;
			_uploadStream = current;
			return current;
		}

		await DisposeQuietlyAsync(current);
		Stream stream = await open(_lifetime.Token);
		if (position > 0)
		{
			if (stream.CanSeek)
			{
				stream.Position = position;
			}
			else
			{
				byte[] skip = new byte[81920];
				long left = position;
				while (left > 0)
				{
					int read = await stream.ReadAsync(skip.AsMemory(0, (int)Math.Min(skip.Length, left)), _lifetime.Token);
					if (read <= 0)
					{
						break;
					}

					left -= read;
				}
			}
		}

		_uploadIndex = index;
		_uploadStream = stream;
		return stream;
	}

	private async Task FailAsync(string message)
	{
		await _engine.WaitAsync(CancellationToken.None);
		try
		{
			await ApplyStepAsync(_session.Cancel(), CancellationToken.None);
		}
		finally
		{
			_engine.Release();
		}

		CompleteFile(ok: false);
		Finish(ZmodemOutcome.Failed, message);
		RaiseChanged();
	}

	private void QueueSend(List<byte> bytes)
	{
		lock (_lock)
		{
			_outbox.AddRange(bytes);
		}

		_ = FlushAsync();
	}

	private async Task FlushAsync()
	{
		await _flushing.WaitAsync();
		try
		{
			while (true)
			{
				byte[] pending;
				lock (_lock)
				{
					if (_outbox.Count == 0)
					{
						return;
					}

					pending = [.. _outbox];
					_outbox.Clear();
				}

				await _stream.SendRawAsync(pending, _lifetime.Token);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogDebug(ex, "Sending ZMODEM protocol bytes failed; the channel is closing.");
		}
		finally
		{
			_flushing.Release();
		}
	}

	/// <summary>Runs an engine intent under the gate, logging anything it throws. Nothing calls back into the caller.</summary>
	private void RunEngine(Func<Task> act)
	{
		async Task RunAsync()
		{
			await _engine.WaitAsync(CancellationToken.None);
			try
			{
				await act();
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "A ZMODEM transfer step failed.");
			}
			finally
			{
				_engine.Release();
			}
		}

		_ = RunAsync();
	}

	private void TrackProgress()
	{
		DateTimeOffset now = _timeProvider.GetUtcNow();
		if (now - _speedAt >= SpeedWindow)
		{
			long moved = _session.FileBytes - _speedBytes;
			double instant = moved / (now - _speedAt).TotalSeconds;
			_bytesPerSecond = _bytesPerSecond == 0 ? instant : (0.6 * _bytesPerSecond) + (0.4 * instant);
			_speedAt = now;
			_speedBytes = _session.FileBytes;
		}

		ReportProgress(force: false);
	}

	private void ReportProgress(bool force)
	{
		DateTimeOffset now = _timeProvider.GetUtcNow();
		if (!force && now - _progressAt < ProgressInterval)
		{
			return;
		}

		_progressAt = now;
		if (_session.FileName is { } name)
		{
			bool receiving = _session.State == ZmodemSessionState.Receiving;
			_progress = new ZmodemProgress(name, receiving, _session.FileBytes, _session.FileTotal, _bytesPerSecond);
		}

		RaiseChanged();
	}

	private void CompleteFile(bool ok)
	{
		_fileDone.TrySetResult(ok);
		_fileDone = NewCompletion();
		_saveStream = null;
	}

	private void CloseUpload()
	{
		if (_uploadStream is { } stream)
		{
			_ = DisposeQuietlyAsync(stream);
			_uploadStream = null;
		}

		_uploadFiles = null;
		_uploadIndex = -1;
	}

	private static async Task DisposeQuietlyAsync(Stream? stream)
	{
		if (stream is not null)
		{
			try
			{
				await stream.DisposeAsync();
			}
			catch (Exception)
			{
				// A host stream that fails to close has nothing left to tell us.
			}
		}
	}

	private void Finish(ZmodemOutcome outcome, string result)
	{
		_phase = ZmodemPhase.Done;
		_outcome = outcome;
		_result = result;
		_offer = null;
		_progress = null;

		string notice = result.Length > 0 ? "zmodem: " + char.ToLowerInvariant(result[0]) + result[1..] : "zmodem.";
		_ = WriteNoticeAsync(notice);

		_completedFiles = 0;
		_completedBytes = 0;
	}

	private void RaiseChanged() => EventRaiser.Raise(Changed, _logger, nameof(Changed));

	/// <summary>A completion notice is a local line in the terminal; writing it must never block the transfer's own steps.</summary>
	private async Task WriteNoticeAsync(string notice)
	{
		try
		{
			await _stream.WriteLocalAsync(TerminalNotice.Format(notice), CancellationToken.None);
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Writing the ZMODEM completion notice failed.");
		}
	}

	private static TaskCompletionSource<bool> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	private static string DescribeCount(int files) => files == 1 ? "1 file" : string.Create(CultureInfo.CurrentCulture, $"{files} files");

	private static string DescribeBytes(long bytes) => bytes switch
	{
		< 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes} B"),
		< 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:F1} kB"),
		< 1024L * 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (1024.0 * 1024):F1} MB"),
		_ => string.Create(CultureInfo.CurrentCulture, $"{bytes / (1024.0 * 1024 * 1024):F1} GB"),
	};

	/// <summary>The name a saved file gets: the offer's last path segment, or none when there is nothing usable in it.</summary>
	private static string SafeName(string name)
	{
		int slash = name.LastIndexOfAny(['/', '\\']);
		string plain = slash >= 0 ? name[(slash + 1)..] : name;
		return RemotePath.IsValidName(plain) ? plain : "";
	}
}
