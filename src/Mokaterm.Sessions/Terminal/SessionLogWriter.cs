using System.Buffers;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Sessions.Terminal;

/// <summary>
/// One session's log file: a terminal sink that appends what the server printed, in the format the settings chose,
/// until the size cap, then says so in the file and takes nothing more. Nothing here may fail a session, so every error
/// ends up in <see cref="Status"/> instead of reaching the pump.
/// </summary>
internal sealed class SessionLogWriter : ITerminalSink, IAsyncDisposable
{
	/// <summary>The scratch buffer is let go past this, so one huge replay does not stay allocated for the whole session.</summary>
	private const int KeptScratchBytes = 1024 * 1024;

	/// <summary>The size a recording states when no view has sized the terminal yet, which is the classic default.</summary>
	private static readonly TerminalSize FallbackSize = new(80, 24);

	private readonly string _folder;
	private readonly string _endpoint;
	private readonly DateTimeOffset _startedAt;
	private readonly long _sizeLimit;
	private readonly SessionLogFormatter _formatter;
	private readonly Func<TerminalSize> _terminalSize;
	private readonly TimeProvider _time;
	private readonly SessionLogRetention? _retention;
	private readonly ILogger _logger;

	// Output arrives one chunk at a time from the attachment's own delivery loop, but a stop comes from wherever the
	// user clicked, so the file is only ever touched while holding this.
	private readonly SemaphoreSlim _gate = new(1, 1);

	// Read from any thread for the toolbar, written while holding the gate.
	private readonly Lock _statusLock = new();

	private ArrayBufferWriter<byte> _scratch = new();
	private FileStream? _file;

	// Held from just before the file is created until it is closed, so no cleanup or delete can take it meanwhile.
	private IDisposable? _writing;
	private long _openedAt;
	private string? _fileName;
	private string? _message;
	private SessionLogState _state = SessionLogState.Recording;
	private long _written;
	private bool _closed;
	private bool _notify;

	/// <param name="endpoint">The session as <c>user@host:port</c>, which the file name and the header are built from.</param>
	/// <param name="startedAt">Local time: it is what someone looking for the file afterwards remembers.</param>
	/// <param name="terminalSize">
	/// The terminal's size now. A sink never hears about a resize, so a format that records sizes asks before each chunk.
	/// </param>
	/// <param name="time">Times the chunks of a timed recording, from the moment its file opens.</param>
	/// <param name="retention">Told which file this is writing, so cleanup leaves it alone; null in tests that need no cleanup.</param>
	public SessionLogWriter(
		string folder,
		string endpoint,
		DateTimeOffset startedAt,
		SessionLogSettings settings,
		Func<TerminalSize> terminalSize,
		TimeProvider time,
		ILogger logger,
		SessionLogRetention? retention = null)
	{
		_retention = retention;
		_folder = folder;
		_endpoint = endpoint;
		_startedAt = startedAt;
		_sizeLimit = 1024L * Math.Clamp(
			settings.SizeLimitKilobytes,
			SessionLogSettings.MinSizeLimitKilobytes,
			SessionLogSettings.MaxSizeLimitKilobytes);
		_formatter = SessionLogFormatter.For(settings.Format);
		_terminalSize = terminalSize;
		_time = time;
		_logger = logger;
	}

	/// <summary>Raised when the file or the state changes, not while output is only growing.</summary>
	public event Action? Changed;

	public SessionLogStatus Status
	{
		get
		{
			lock (_statusLock)
			{
				return new SessionLogStatus
				{
					State = _state,
					FileName = _fileName,
					Bytes = Interlocked.Read(ref _written),
					Message = _message,
				};
			}
		}
	}

	public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		// What the server printed, and only that. A sink cannot see keystrokes, and no tap for them belongs here or
		// anywhere near it: this is a plain file, and a password typed at a prompt would be in it in the clear.
		if (data.IsEmpty)
		{
			return;
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (_closed || _state != SessionLogState.Recording)
			{
				return;
			}

			await AppendAsync(data, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// A log that cannot be written is not worth a session: the toolbar shows the failure and the pump carries on.
			Fail(ex);
		}
		finally
		{
			_gate.Release();
		}

		Notify();
	}

	/// <summary>Closes the file. Detach the sink first, so nothing is queued behind this.</summary>
	public async ValueTask DisposeAsync()
	{
		await _gate.WaitAsync(CancellationToken.None);
		try
		{
			if (_closed)
			{
				return;
			}

			_closed = true;
			if (_file is { } file)
			{
				_file = null;
				await file.DisposeAsync();
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning("Closing a session log failed: {Failure}", LogSafe.Describe(ex));
		}
		finally
		{
			ReleaseWriting();

			// The semaphore is not disposed: a write that started before the detach would find it gone. It holds no
			// wait handle, so there is nothing to release.
			_gate.Release();
		}
	}

	private TerminalSize CurrentSize()
	{
		TerminalSize size = _terminalSize();
		return size.IsValid ? size : FallbackSize;
	}

	private async ValueTask AppendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		FileStream file = _file ?? await CreateAsync(cancellationToken);
		TimeSpan elapsed = _time.GetElapsedTime(_openedAt);
		long room = _sizeLimit - Interlocked.Read(ref _written);
		try
		{
			_scratch.ResetWrittenCount();
			bool whole = _formatter.Append(data.Span, elapsed, CurrentSize(), room, _scratch);
			if (_scratch.WrittenCount > 0)
			{
				await file.WriteAsync(_scratch.WrittenMemory, cancellationToken);
				_ = Interlocked.Add(ref _written, _scratch.WrittenCount);
			}

			if (!whole)
			{
				await FillUpAsync(file, elapsed, cancellationToken);
			}
		}
		finally
		{
			if (_scratch.Capacity > KeptScratchBytes)
			{
				_scratch = new ArrayBufferWriter<byte>();
			}
		}
	}

	private async ValueTask<FileStream> CreateAsync(CancellationToken cancellationToken)
	{
		// A transcript is as private as the vault beside it, so it gets the same modes.
		OwnerOnlyFiles.CreateDirectory(_folder);

		for (int attempt = 0; ; attempt++)
		{
			string name = SessionLogNames.Build(_endpoint, _startedAt, attempt, _formatter.Extension);
			string path = Path.Combine(_folder, name);
			FileStreamOptions options = OwnerOnlyFiles.Owned(new FileStreamOptions
			{
				Mode = FileMode.CreateNew,
				Access = FileAccess.Write,
				Share = FileShare.Read,
				Options = FileOptions.Asynchronous,

				// No buffer: chunks arrive at up to 32 KiB anyway, and every one landing in the file as it is written is
				// what lets the log be read while the session still runs.
				BufferSize = 0,
			});

			// Marked before it exists, so there is no moment in which cleanup could see the new file as nobody's.
			IDisposable? writing = _retention?.MarkWriting(path);
			FileStream file;
			try
			{
				file = new FileStream(path, options);
			}
			catch (IOException) when (attempt < SessionLogNames.MaxAttempts && File.Exists(path))
			{
				writing?.Dispose();
				continue;
			}
			catch
			{
				writing?.Dispose();
				throw;
			}

			_file = file;
			_writing = writing;
			SetStatus(SessionLogState.Recording, message: null, name);

			// A timed recording counts from here, so its first chunk lands at the start.
			_openedAt = _time.GetTimestamp();
			await file.WriteAsync(_formatter.Header(_endpoint, _startedAt, CurrentSize()), cancellationToken);
			return file;
		}
	}

	private async ValueTask FillUpAsync(FileStream file, TimeSpan elapsed, CancellationToken cancellationToken)
	{
		long kilobytes = _sizeLimit / 1024;
		SetStatus(SessionLogState.Full, string.Create(CultureInfo.CurrentCulture, $"The {kilobytes} KB limit was reached."), _fileName);
		await file.WriteAsync(_formatter.LimitNotice(kilobytes, elapsed), cancellationToken);
		_file = null;
		await file.DisposeAsync();
		ReleaseWriting();
	}

	private void Fail(Exception exception)
	{
		SetStatus(SessionLogState.Failed, "The log file could not be written.", _fileName);
		_logger.LogWarning("Writing a session log failed: {Failure}", LogSafe.Describe(exception));
		FileStream? file = _file;
		_file = null;
		try
		{
			file?.Dispose();
		}
		catch (Exception ex) when (ex is IOException or ObjectDisposedException)
		{
			// Already broken, and the state above is what the user sees.
		}

		ReleaseWriting();
	}

	private void ReleaseWriting() => Interlocked.Exchange(ref _writing, null)?.Dispose();

	private void SetStatus(SessionLogState state, string? message, string? fileName)
	{
		lock (_statusLock)
		{
			_state = state;
			_message = message;
			_fileName = fileName;
		}

		// Raised after the gate is released: a handler is free to stop this writer.
		_notify = true;
	}

	private void Notify()
	{
		if (_notify)
		{
			_notify = false;
			Changed?.Invoke();
		}
	}
}
