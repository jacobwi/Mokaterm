using System.Text;
using System.Threading.Channels;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.DevHost.Demo.Terminal;

/// <summary>
/// The byte stream of the demo shell. Output goes through a bounded channel, so a slow view holds a command like
/// <c>seq</c> back the way a real pty does. Commands run as jobs beside the input path: keys typed meanwhile wait their
/// turn, and Ctrl+C stops the job.
/// </summary>
internal sealed class DemoTerminalChannel : ITerminalChannel
{
	// Small, like a pty buffer: output already queued still shows after Ctrl+C, so a large queue delays the ^C.
	private const int OutputCapacity = 16;
	private const int MaxTypeahead = 4096;

	private readonly Channel<byte[]> _output = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(OutputCapacity)
	{
		SingleReader = true,
		FullMode = BoundedChannelFullMode.Wait,
	});

	// Not disposed: a job finishing after the channel closed may still wait on or release it.
	private readonly SemaphoreSlim _inputGate = new(1, 1);
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
	private readonly TerminalKeyReader _keys = new();
	private readonly Queue<TerminalKey> _typeahead = new();
	private readonly DemoShell _shell;
	private readonly LineEditor _editor;
	private readonly ShellOutput _echo;
	private readonly Action _ended;
	private readonly ILogger _logger;

	// Only read or changed while holding the input gate. The interrupt flag outlives a command that finished just as
	// Ctrl+C arrived, so the ^C still shows.
	private CancellationTokenSource? _job;
	private bool _interruptRequested;
	private byte[]? _pending;
	private int _pendingOffset;
	private int _disposed;

	/// <param name="ended">Called once the login shell exits and its last output is queued.</param>
	public DemoTerminalChannel(DemoShell shell, Action ended, ILogger logger)
	{
		_shell = shell;
		_editor = new LineEditor(shell.History);
		_echo = new ShellOutput(_output.Writer);
		_ended = ended;
		_logger = logger;
		StartJob(shell.GreetAsync);
	}

	public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		if (buffer.IsEmpty)
		{
			return 0;
		}

		while (_pending is null)
		{
			if (Volatile.Read(ref _disposed) != 0)
			{
				return 0;
			}

			if (_output.Reader.TryRead(out byte[]? chunk))
			{
				_pending = chunk;
				_pendingOffset = 0;
			}
			else if (!await _output.Reader.WaitToReadAsync(cancellationToken))
			{
				return 0;
			}
		}

		// Takes as many queued chunks as fit, so a burst of output reaches the view in few writes.
		int count = 0;
		while (_pending is not null && count < buffer.Length)
		{
			int take = Math.Min(buffer.Length - count, _pending.Length - _pendingOffset);
			_pending.AsSpan(_pendingOffset, take).CopyTo(buffer.Span[count..]);
			count += take;
			_pendingOffset += take;
			if (_pendingOffset == _pending.Length)
			{
				_pending = _output.Reader.TryRead(out byte[]? next) ? next : null;
				_pendingOffset = 0;
			}
		}

		return count;
	}

	public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		if (data.IsEmpty || Volatile.Read(ref _disposed) != 0)
		{
			return;
		}

		await _inputGate.WaitAsync(cancellationToken);
		try
		{
			char[] chars = new char[Encoding.UTF8.GetMaxCharCount(data.Length)];
			int count = _decoder.GetChars(data.Span, chars, flush: false);
			foreach (TerminalKey key in _keys.Read(chars.AsSpan(0, count)))
			{
				if (_job is null)
				{
					await HandleKeyAsync(key, cancellationToken);
				}
				else
				{
					QueueWhileBusy(key);
				}
			}
		}
		catch (ChannelClosedException)
		{
			// The shell exited or the channel closed; typed input has nowhere to go.
		}
		finally
		{
			_inputGate.Release();
		}
	}

	public ValueTask ResizeAsync(TerminalSize size, CancellationToken cancellationToken)
	{
		if (size.IsValid)
		{
			_shell.Size = size;
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_lifetime.Cancel();
			_output.Writer.TryComplete();
		}

		return ValueTask.CompletedTask;
	}

	private void QueueWhileBusy(TerminalKey key)
	{
		if (key.Kind == TerminalKeyKind.Interrupt)
		{
			// Like a tty's interrupt: stop the job and drop whatever was typed ahead.
			_interruptRequested = true;
			_job?.Cancel();
			_typeahead.Clear();
		}
		else if (_typeahead.Count < MaxTypeahead)
		{
			_typeahead.Enqueue(key);
		}
	}

	private async ValueTask HandleKeyAsync(TerminalKey key, CancellationToken cancellationToken)
	{
		switch (key.Kind)
		{
			case TerminalKeyKind.Text:
				await _echo.SendAsync(_editor.Insert(key.Text), cancellationToken);
				break;

			case TerminalKeyKind.Backspace:
				await _echo.SendAsync(_editor.Backspace(), cancellationToken);
				break;

			case TerminalKeyKind.Enter:
			{
				string line = _editor.Submit();
				await _echo.SendAsync("\r\n", cancellationToken);
				StartJob((output, token) => _shell.ExecuteAsync(line, output, token));
				break;
			}

			case TerminalKeyKind.Interrupt:
				_editor.Discard();
				await _echo.SendAsync("^C\r\n" + _shell.Prompt, cancellationToken);
				break;

			case TerminalKeyKind.ClearScreen:
				await _echo.SendAsync(Ansi.ClearScreen + _shell.Prompt + _editor.Text, cancellationToken);
				break;

			case TerminalKeyKind.EndOfFile when _editor.IsEmpty:
				StartJob(_shell.ExitAsync);
				break;

			case TerminalKeyKind.Tab:
				// No completion here; the bell at least shows how the terminal renders one.
				await _echo.SendAsync("\u0007", cancellationToken);
				break;

			case TerminalKeyKind.HistoryPrevious:
				await _echo.SendAsync(_editor.Previous(_shell.VisiblePrompt), cancellationToken);
				break;

			case TerminalKeyKind.HistoryNext:
				await _echo.SendAsync(_editor.Next(_shell.VisiblePrompt), cancellationToken);
				break;
		}
	}

	/// <summary>Runs a command in the background. Call while holding the input gate.</summary>
	private void StartJob(Func<ShellOutput, CancellationToken, Task<bool>> command)
	{
		CancellationTokenSource job = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		_job = job;
		_ = RunJobAsync(command, job);
	}

	private async Task RunJobAsync(Func<ShellOutput, CancellationToken, Task<bool>> command, CancellationTokenSource job)
	{
		// Returns to the caller first: it holds the input gate that the end of this job needs.
		await Task.Yield();

		ShellOutput output = new(_output.Writer);
		bool keepRunning = true;
		try
		{
			keepRunning = await command(output, job.Token);
			await output.FlushAsync(job.Token);
		}
		catch (OperationCanceledException) when (job.IsCancellationRequested)
		{
			// Ctrl+C or the channel closing; the checks below tell which.
		}
		catch (ChannelClosedException)
		{
			return;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "A demo shell command failed");
		}

		try
		{
			await _inputGate.WaitAsync(_lifetime.Token);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		try
		{
			_job = null;
			job.Dispose();
			bool interrupted = _interruptRequested;
			_interruptRequested = false;
			if (!keepRunning)
			{
				_output.Writer.TryComplete();
				_ended();
				return;
			}

			await _echo.SendAsync((interrupted ? "^C\r\n" : "") + _shell.Prompt, _lifetime.Token);
			while (_job is null && _typeahead.TryDequeue(out TerminalKey key))
			{
				await HandleKeyAsync(key, _lifetime.Token);
			}
		}
		catch (Exception ex) when (ex is ChannelClosedException or OperationCanceledException)
		{
			// Closed while showing the prompt; nothing is left to show it on.
		}
		finally
		{
			_inputGate.Release();
		}
	}
}
