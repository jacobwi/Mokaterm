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
/// One session's log file: a terminal sink that appends what the server printed until the size cap, then says so in the
/// file and takes nothing more. Nothing here may fail a session, so every error ends up in <see cref="Status"/> instead
/// of reaching the pump.
/// </summary>
internal sealed class SessionLogWriter : ITerminalSink, IAsyncDisposable
{
	/// <summary>Names tried before giving up, for two sessions to the same host inside one second.</summary>
	private const int NameAttempts = 20;

	/// <summary>The header's second line: what the file holds, and what it deliberately does not.</summary>
	private const string HeaderNotice =
		"[mokaterm] what the server printed, opening with the scrollback that was already on screen. Nothing typed is recorded.\r\n";

	private static readonly UTF8Encoding NoticeEncoding = new(encoderShouldEmitUTF8Identifier: false);

	private readonly string _folder;
	private readonly string _endpoint;
	private readonly DateTimeOffset _startedAt;
	private readonly long _sizeLimit;
	private readonly TerminalTextFilter? _filter;
	private readonly ILogger _logger;

	// Output arrives one chunk at a time from the attachment's own delivery loop, but a stop comes from wherever the
	// user clicked, so the file is only ever touched while holding this.
	private readonly SemaphoreSlim _gate = new(1, 1);

	// Read from any thread for the toolbar, written while holding the gate.
	private readonly Lock _statusLock = new();

	private FileStream? _file;
	private string? _fileName;
	private string? _message;
	private SessionLogState _state = SessionLogState.Recording;
	private long _written;
	private bool _closed;
	private bool _notify;

	/// <param name="endpoint">The session as <c>user@host:port</c>, which the file name and the header are built from.</param>
	/// <param name="startedAt">Local time: it is what someone looking for the file afterwards remembers.</param>
	public SessionLogWriter(string folder, string endpoint, DateTimeOffset startedAt, SessionLogSettings settings, ILogger logger)
	{
		_folder = folder;
		_endpoint = endpoint;
		_startedAt = startedAt;
		_sizeLimit = 1024L * Math.Clamp(
			settings.SizeLimitKilobytes,
			SessionLogSettings.MinSizeLimitKilobytes,
			SessionLogSettings.MaxSizeLimitKilobytes);
		_filter = settings.Format == SessionLogFormat.PlainText ? new TerminalTextFilter() : null;
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
			// The semaphore is not disposed: a write that started before the detach would find it gone. It holds no
			// wait handle, so there is nothing to release.
			_gate.Release();
		}
	}

	/// <summary>Turns a session into a name a human can find again: <c>web01-root-20260926-142530.log</c>.</summary>
	internal static string BuildFileName(string endpoint, DateTimeOffset startedAt, int attempt)
	{
		string user = "";
		string host = endpoint;
		int at = endpoint.IndexOf('@', StringComparison.Ordinal);
		if (at >= 0)
		{
			user = endpoint[..at];
			host = endpoint[(at + 1)..];
		}

		// The port goes: an IPv6 address is full of colons, and the one that matters is the last.
		int colon = host.LastIndexOf(':');
		if (colon > 0)
		{
			host = host[..colon];
		}

		StringBuilder name = new();
		name.Append(Slug(host, 48));
		if (Slug(user, 32) is { Length: > 0 } slug)
		{
			name.Append('-').Append(slug);
		}

		name.Append(CultureInfo.InvariantCulture, $"-{startedAt:yyyyMMdd-HHmmss}");
		if (attempt > 0)
		{
			name.Append(CultureInfo.InvariantCulture, $"-{attempt}");
		}

		return name.Append(".log").ToString();
	}

	/// <summary>Keeps letters, digits, dots, dashes and underscores; every other run, an IPv6 colon included, becomes one underscore.</summary>
	private static string Slug(string value, int maxLength)
	{
		StringBuilder slug = new(value.Length);
		bool filler = false;
		foreach (char character in value)
		{
			if (slug.Length >= maxLength)
			{
				break;
			}

			if (char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
			{
				slug.Append(character);
				filler = false;
			}
			else if (!filler && slug.Length > 0)
			{
				slug.Append('_');
				filler = true;
			}
		}

		return slug.ToString().Trim('_', '.', '-');
	}

	private async ValueTask AppendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		FileStream file = _file ?? await CreateAsync(cancellationToken);
		byte[]? rented = null;
		try
		{
			ReadOnlyMemory<byte> payload = data;
			if (_filter is { } filter)
			{
				rented = ArrayPool<byte>.Shared.Rent(data.Length);
				payload = rented.AsMemory(0, filter.Filter(data.Span, rented));
			}

			if (payload.IsEmpty)
			{
				return;
			}

			long room = _sizeLimit - Interlocked.Read(ref _written);
			if (payload.Length > room)
			{
				// Cut at the cap rather than drop the chunk: a chunk can be larger than the whole cap (the replay buffer a
				// recording starts with is), and a log that stops mid-line beats one that never started.
				if (room > 0)
				{
					await file.WriteAsync(payload[..(int)room], cancellationToken);
					_ = Interlocked.Add(ref _written, room);
				}

				await FillUpAsync(file, cancellationToken);
				return;
			}

			await file.WriteAsync(payload, cancellationToken);
			_ = Interlocked.Add(ref _written, payload.Length);
		}
		finally
		{
			if (rented is not null)
			{
				ArrayPool<byte>.Shared.Return(rented);
			}
		}
	}

	private async ValueTask<FileStream> CreateAsync(CancellationToken cancellationToken)
	{
		// A transcript is as private as the vault beside it, so it gets the same modes.
		OwnerOnlyFiles.CreateDirectory(_folder);

		for (int attempt = 0; ; attempt++)
		{
			string name = BuildFileName(_endpoint, _startedAt, attempt);
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

			FileStream file;
			try
			{
				file = new FileStream(path, options);
			}
			catch (IOException) when (attempt < NameAttempts && File.Exists(path))
			{
				continue;
			}

			_file = file;
			SetStatus(SessionLogState.Recording, message: null, name);
			await WriteHeaderAsync(file, cancellationToken);
			return file;
		}
	}

	/// <summary>Says what the file is, when it started and what it does not hold, for whoever opens it months later.</summary>
	private async ValueTask WriteHeaderAsync(FileStream file, CancellationToken cancellationToken)
	{
		string header = string.Create(
			CultureInfo.InvariantCulture,
			$"[mokaterm] session log: {_endpoint}, started {_startedAt:yyyy-MM-dd HH:mm:ss zzz}\r\n{HeaderNotice}");
		await file.WriteAsync(NoticeEncoding.GetBytes(header), cancellationToken);
	}

	private async ValueTask FillUpAsync(FileStream file, CancellationToken cancellationToken)
	{
		long kilobytes = _sizeLimit / 1024;
		SetStatus(SessionLogState.Full, string.Create(CultureInfo.CurrentCulture, $"The {kilobytes} KB limit was reached."), _fileName);
		string notice = string.Create(
			CultureInfo.CurrentCulture,
			$"\r\n[mokaterm] the {kilobytes} KB limit was reached; the rest of this session is not recorded.\r\n");
		await file.WriteAsync(NoticeEncoding.GetBytes(notice), cancellationToken);
		_file = null;
		await file.DisposeAsync();
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
	}

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
