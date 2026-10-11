using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Modules.Local.Pty.Windows;

/// <summary>
/// A program in a Windows pseudo console (ConPTY). The console runs in a conhost of its own, and this end talks to it
/// through two pipes: what is typed goes down one, and VT encoded UTF-8 comes back up the other, read continuously by
/// a thread of this terminal's own.
/// </summary>
/// <remarks>
/// <para>
/// Three things end a terminal and all of them go through <see cref="CloseAsync"/>, once: the program exiting, the
/// output pipe ending under it, and <see cref="DisposeAsync"/>. The order there is the point. Input stops first. Then
/// ClosePseudoConsole, which before Windows 11 24H2 waits until conhost has written its last frame, so the reader keeps
/// draining meanwhile or the close never returns. Then the reader runs to the end of the pipe. Only then does the job
/// go, which ends whatever the program left running.
/// </para>
/// <para>
/// conhost does not end when the program does; it waits for ClosePseudoConsole. So the exit is watched on the process
/// handle, and the output pipe only ends after the close.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class ConPtyTerminal : IPseudoTerminal
{
	private const int ReadBufferSize = 16 * 1024;

	/// <summary>How long output has to be quiet after the program exits before the console is closed.</summary>
	private const int SettleQuietMilliseconds = 100;

	/// <summary>The longest a program that exited while still printing is waited for.</summary>
	private const int SettleLimitMilliseconds = 1_000;

	private const int SettlePollMilliseconds = 20;

	/// <summary>
	/// The longest each step of a close waits. A close runs on the way out of a tab or of the app, so a conhost that
	/// never answers is left behind rather than allowed to hold either.
	/// </summary>
	private static readonly TimeSpan StepLimit = TimeSpan.FromSeconds(3);

	/// <summary>
	/// When conhost ends first, its pipe closes a moment before the process handle is signalled. The exit code is what
	/// tells a program that finished from one that was lost, so it is worth this wait.
	/// </summary>
	private static readonly TimeSpan ExitGrace = TimeSpan.FromMilliseconds(500);

	private readonly SafePseudoConsoleHandle _console;
	private readonly FileStream _input;
	private readonly FileStream _output;
	private readonly SafeProcessHandle _process;
	private readonly SafeKernelHandle _job;
	private readonly IPseudoTerminalOutput _sink;
	private readonly TerminalWriteLock _writeLock = new();

	// Resizes hold this across the native call and the close sets _consoleClosed under it, so no resize can be inside
	// ResizePseudoConsole while the console is being freed.
	private readonly Lock _consoleLock = new();
	private readonly Lock _closeLock = new();
	private readonly TaskCompletionSource _outputEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _processEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource<int?> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private ProcessExitWaitHandle? _exitWait;
	private RegisteredWaitHandle? _exitRegistration;
	private Task? _closing;
	private bool _consoleClosed;
	private volatile bool _inputClosed;
	private volatile bool _disposeRequested;
	private long _lastOutputAt;
	private int _disposed;

	private ConPtyTerminal(
		SafePseudoConsoleHandle console,
		SafeFileHandle input,
		SafeFileHandle output,
		SafeProcessHandle process,
		SafeKernelHandle job,
		int processId,
		IPseudoTerminalOutput sink)
	{
		_console = console;
		_input = new FileStream(input, FileAccess.Write, bufferSize: 0);
		_output = new FileStream(output, FileAccess.Read, bufferSize: 0);
		_process = process;
		_job = job;
		_sink = sink;
		ProcessId = processId;
	}

	public int ProcessId { get; }

	public Task<int?> Exited => _exited.Task;

	/// <summary>Starts <paramref name="launch"/> in a new pseudo console. Whatever this throws comes from Windows.</summary>
	public static ConPtyTerminal Start(PseudoTerminalLaunch launch, TerminalSize size, IPseudoTerminalOutput output)
	{
		ArgumentNullException.ThrowIfNull(launch);
		ArgumentNullException.ThrowIfNull(output);

		SafeFileHandle? inputRead = null;
		SafeFileHandle? inputWrite = null;
		SafeFileHandle? outputRead = null;
		SafeFileHandle? outputWrite = null;
		SafePseudoConsoleHandle? console = null;
		SafeKernelHandle? job = null;
		SafeProcessHandle? process = null;
		try
		{
			CreatePipe(out inputRead, out inputWrite);
			CreatePipe(out outputRead, out outputWrite);
			Marshal.ThrowExceptionForHR(ConPtyNative.CreatePseudoConsole(Coord.From(size), inputRead, outputWrite, 0, out console));

			// conhost holds duplicates of its two ends. Ours go now: while this process still holds the output pipe's
			// write end, the read end never reaches its end and the reader would outlive the console.
			inputRead.Dispose();
			outputWrite.Dispose();

			job = CreateKillOnCloseJob();
			(process, int processId) = CreateProcess(launch, console, job);
			ConPtyTerminal terminal = new(console, inputWrite, outputRead, process, job, processId, output);
			terminal.Begin();
			return terminal;
		}
		catch
		{
			// The read end first, so a console that already wrote something cannot hold its own close waiting for a reader.
			outputRead?.Dispose();
			outputWrite?.Dispose();
			inputRead?.Dispose();
			inputWrite?.Dispose();
			job?.Dispose();
			process?.Dispose();
			console?.Dispose();
			throw;
		}
	}

	public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		if (data.IsEmpty || _inputClosed || !await _writeLock.TryEnterAsync(cancellationToken))
		{
			return;
		}

		try
		{
			if (!_inputClosed)
			{
				await _input.WriteAsync(data, cancellationToken);
			}
		}
		catch (Exception ex) when (ex is IOException or ObjectDisposedException)
		{
			// The console is closing, so what was typed has nowhere to go.
		}
		finally
		{
			_writeLock.Release();
		}
	}

	public void Resize(TerminalSize size)
	{
		if (!size.IsValid)
		{
			return;
		}

		lock (_consoleLock)
		{
			if (!_consoleClosed)
			{
				// A refused size leaves the console as it was, which is all a caller could do about it either.
				_ = ConPtyNative.ResizePseudoConsole(_console, Coord.From(size));
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_disposeRequested = true;
		await CloseOnceAsync();
	}

	private static void CreatePipe(out SafeFileHandle read, out SafeFileHandle write)
	{
		if (!ConPtyNative.CreatePipe(out read, out write, 0, 0))
		{
			throw new Win32Exception();
		}
	}

	/// <summary>
	/// A job that ends everything in it when its last handle closes. The shell goes in before it runs a single
	/// instruction, so nothing it starts can be outside, and closing the tab cannot leave any of it running.
	/// </summary>
	private static SafeKernelHandle CreateKillOnCloseJob()
	{
		SafeKernelHandle job = ConPtyNative.CreateJobObjectW(0, null);
		if (job.IsInvalid)
		{
			Win32Exception failure = new();
			job.Dispose();
			throw failure;
		}

		JobLimits limits = new(ConPtyNative.KillOnJobClose);
		if (!ConPtyNative.SetInformationJobObject(job, ConPtyNative.JobObjectExtendedLimitInformation, ref limits, Marshal.SizeOf<JobLimits>()))
		{
			Win32Exception failure = new();
			job.Dispose();
			throw failure;
		}

		return job;
	}

	/// <summary>
	/// Creates the program attached to <paramref name="console"/>, suspended until it is in <paramref name="job"/>. No
	/// handle is inherited: the console is all it gets.
	/// </summary>
	private static (SafeProcessHandle Process, int ProcessId) CreateProcess(
		PseudoTerminalLaunch launch,
		SafePseudoConsoleHandle console,
		SafeKernelHandle job)
	{
		char[] commandLine = (WindowsCommandLine.Build(launch.Program, launch.Arguments, launch.ExtraArguments) + "\0").ToCharArray();
		char[] environment = WindowsEnvironmentBlock.Build(launch.Environment);
		nint attributes = 0;
		bool initialized = false;
		bool referenced = false;
		console.DangerousAddRef(ref referenced);
		try
		{
			// The first call fails on purpose: it is how the list says how much memory it needs.
			nint size = 0;
			_ = ConPtyNative.InitializeProcThreadAttributeList(0, 1, 0, ref size);
			attributes = Marshal.AllocHGlobal(size);
			if (!ConPtyNative.InitializeProcThreadAttributeList(attributes, 1, 0, ref size))
			{
				throw new Win32Exception();
			}

			initialized = true;
			if (!ConPtyNative.UpdateProcThreadAttribute(
				attributes,
				0,
				ConPtyNative.PseudoConsoleAttribute,
				console.DangerousGetHandle(),
				nint.Size,
				0,
				0))
			{
				throw new Win32Exception();
			}

			StartupInfoEx startup = new(attributes);
			uint flags = ConPtyNative.ExtendedStartupInfoPresent | ConPtyNative.CreateUnicodeEnvironment | ConPtyNative.CreateSuspended;
			if (!ConPtyNative.CreateProcessW(
				launch.Program,
				commandLine,
				0,
				0,
				inheritHandles: false,
				flags,
				environment,
				launch.WorkingDirectory,
				ref startup,
				out ProcessInformation created))
			{
				throw new Win32Exception();
			}

			SafeProcessHandle process = new(created.Process, ownsHandle: true);
			using SafeKernelHandle thread = new(created.Thread);
			try
			{
				if (!ConPtyNative.AssignProcessToJobObject(job, process) || ConPtyNative.ResumeThread(thread) == ConPtyNative.ResumeThreadFailed)
				{
					throw new Win32Exception();
				}

				return (process, created.ProcessId);
			}
			catch
			{
				_ = ConPtyNative.TerminateProcess(process, 1);
				process.Dispose();
				throw;
			}
		}
		finally
		{
			if (initialized)
			{
				ConPtyNative.DeleteProcThreadAttributeList(attributes);
			}

			if (attributes != 0)
			{
				Marshal.FreeHGlobal(attributes);
			}

			if (referenced)
			{
				console.DangerousRelease();
			}
		}
	}

	private static async Task<bool> WithinAsync(Task task, TimeSpan limit)
	{
		try
		{
			await task.WaitAsync(limit);
			return true;
		}
		catch (TimeoutException)
		{
			return false;
		}
	}

	private void Begin()
	{
		Volatile.Write(ref _lastOutputAt, Environment.TickCount64);
		Thread reader = new(ReadOutput)
		{
			IsBackground = true,
			Name = "Mokaterm local shell output",
		};
		reader.Start();

		_exitWait = new ProcessExitWaitHandle(_process);
		_exitRegistration = ThreadPool.UnsafeRegisterWaitForSingleObject(
			_exitWait,
			static (state, _) => ((ConPtyTerminal)state!).OnProcessExited(),
			this,
			Timeout.Infinite,
			executeOnlyOnce: true);
	}

	/// <summary>
	/// The output loop, on a thread of its own: a synchronous pipe read blocks until conhost writes, and a console whose
	/// output nobody reads cannot close. Once the output is no longer wanted the rest is still read, and dropped.
	/// </summary>
	private void ReadOutput()
	{
		byte[] buffer = new byte[ReadBufferSize];
		bool wanted = true;
		try
		{
			while (true)
			{
				int read = _output.Read(buffer, 0, buffer.Length);
				if (read == 0)
				{
					return;
				}

				Volatile.Write(ref _lastOutputAt, Environment.TickCount64);
				if (wanted)
				{
					wanted = _sink.Write(buffer.AsSpan(0, read));
				}
			}
		}
		catch (Exception)
		{
			// A broken pipe ends the output the way its end does. Anything else is caught too: this is a thread of
			// our own, and an exception escaping it would end the whole app.
		}
		finally
		{
			_output.Dispose();
			_outputEnded.TrySetResult();
			_ = CloseOnceAsync();
		}
	}

	private void OnProcessExited()
	{
		long exitedAt = Environment.TickCount64;
		_processEnded.TrySetResult();
		_ = CloseAfterSettlingAsync(exitedAt);
	}

	/// <summary>
	/// conhost paints on its own clock, so what a program wrote just before it exited may still be on its way; the
	/// console closes once the output has been quiet for a moment.
	/// </summary>
	private async Task CloseAfterSettlingAsync(long exitedAt)
	{
		while (true)
		{
			long now = Environment.TickCount64;
			long quietSince = Math.Max(exitedAt, Volatile.Read(ref _lastOutputAt));
			if (now - quietSince >= SettleQuietMilliseconds || now - exitedAt >= SettleLimitMilliseconds)
			{
				break;
			}

			await Task.Delay(SettlePollMilliseconds);
		}

		await CloseOnceAsync();
	}

	private Task CloseOnceAsync()
	{
		lock (_closeLock)
		{
			return _closing ??= Task.Run(CloseAsync);
		}
	}

	private async Task CloseAsync()
	{
		// A program this close ends has not exited: its code is only what closing the console made of it (0xC000013A).
		bool exitedFirst = _processEnded.Task.IsCompleted;
		int? exitCode = null;
		try
		{
			await CloseInputAsync();

			lock (_consoleLock)
			{
				_consoleClosed = true;
			}

			await WithinAsync(Task.Run(_console.Dispose), StepLimit);
			await WithinAsync(_outputEnded.Task, StepLimit);
			if (!_disposeRequested)
			{
				await WithinAsync(_processEnded.Task, ExitGrace);
			}

			bool exitedOnItsOwn = (exitedFirst || !_disposeRequested) && _processEnded.Task.IsCompleted;
			exitCode = exitedOnItsOwn && ConPtyNative.GetExitCodeProcess(_process, out uint code) ? unchecked((int)code) : null;
		}
		finally
		{
			// Last, and whatever happened above: the job ends the program and everything it started that still runs.
			_job.Dispose();
			if (await WithinAsync(_processEnded.Task, StepLimit))
			{
				// Unregistering is only safe once the wait has fired; one that never does keeps its handle alive instead.
				_exitRegistration?.Unregister(null);
				_exitWait?.Dispose();
			}

			_process.Dispose();
			_exited.TrySetResult(exitCode);
		}
	}

	/// <summary>
	/// Stops input: no write starts after this, and the pipe closes once a write already in it is done. A write that
	/// never finishes (conhost stopped reading) is not waited for past the limit; the pipe breaks under it when conhost goes.
	/// </summary>
	private async Task CloseInputAsync()
	{
		_inputClosed = true;
		bool held = false;
		using CancellationTokenSource limit = new(StepLimit);
		try
		{
			held = await _writeLock.TryEnterAsync(limit.Token);
		}
		catch (OperationCanceledException)
		{
			// Closed without the lock; the write in flight keeps the handle open until it returns.
		}

		try
		{
			await _input.DisposeAsync();
		}
		catch (IOException)
		{
			// Nothing is buffered, so there is nothing a failed close could lose.
		}
		finally
		{
			if (held)
			{
				_writeLock.Release();
			}
		}
	}
}
