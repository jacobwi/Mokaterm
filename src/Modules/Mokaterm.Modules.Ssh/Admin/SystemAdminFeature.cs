using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.FileSystem;

namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>
/// The system view's state for one session: what the server is (detected once and kept until asked again), root
/// mode, and the change that is running or ran last. It belongs to the session, so closing the dialog neither repeats
/// detection nor stops a package upgrade halfway; closing the session ends both.
/// </summary>
internal sealed class SystemAdminFeature : IAsyncDisposable
{
	private readonly IAdminShell _shell;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly CancellationTokenSource _closed = new();
	private readonly Lock _lock = new();
	private Task<SystemProfile>? _detection;
	private AdminActionResult? _running;
	private AdminActionResult? _last;
	private int _disposed;

	/// <param name="rootFiles">The session's one root file system, for the files a page edits as root; null when the session has no files. The session owns it.</param>
	public SystemAdminFeature(IAdminShell shell, TimeProvider time, ILogger logger, SessionRootFiles? rootFiles = null)
	{
		_shell = shell;
		_time = time;
		_logger = logger;
		RootFiles = rootFiles;
	}

	/// <summary>Raised after detection, root mode or an action changed. Handlers may run on any thread.</summary>
	public event Action? Changed;

	/// <summary>The last detection that finished, or null before the first.</summary>
	public SystemProfile? Profile { get; private set; }

	/// <summary><c>user@host</c>.</summary>
	public string Account => _shell.Account;

	/// <summary>How this session logged in, or null when the session does not say.</summary>
	public AuthenticationMethod? LoginMethod => _shell.LoginMethod;

	/// <summary>The session's root file system, shared with the Docker manager, or null for a session without files.</summary>
	public SessionRootFiles? RootFiles { get; }

	/// <summary>The session logged in as root, so nothing goes through sudo.</summary>
	public bool LoginIsRoot => Profile?.Facts.IsRoot == true;

	/// <summary>sudo is ready and held for this session.</summary>
	public bool RootModeOn => _shell.HasRoot;

	/// <summary>Commands marked to run as root can run now without asking anything.</summary>
	public bool CanActAsRoot => LoginIsRoot || RootModeOn;

	/// <summary>Whether changes that send stdin (a crontab, a password) can run on this session.</summary>
	public bool SupportsInput => _shell.SupportsInput;

	/// <summary>Why root is out of reach on this session, or null.</summary>
	public string? RootBlockedReason => LoginIsRoot ? null : _shell.ElevationBlockedReason;

	/// <summary>The change running now, if any. Only one runs at a time.</summary>
	public AdminActionResult? Running
	{
		get
		{
			lock (_lock)
			{
				return _running;
			}
		}
	}

	/// <summary>The change that ran last, for the result line. It stays until the next one, and survives the dialog closing.</summary>
	public AdminActionResult? Last
	{
		get
		{
			lock (_lock)
			{
				return _last;
			}
		}
	}

	/// <summary>
	/// What the server is. The first call runs the probe and later ones get the same answer until
	/// <paramref name="refresh"/> asks again; a probe that failed is tried again on the next call.
	/// </summary>
	/// <exception cref="AdminException">The probe could not run.</exception>
	public Task<SystemProfile> DetectAsync(bool refresh, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		Task<SystemProfile> detection;
		lock (_lock)
		{
			if (refresh || _detection is null || _detection.IsFaulted || _detection.IsCanceled)
			{
				_detection = DetectCoreAsync();
			}

			detection = _detection;
		}

		// The probe belongs to the session: a view that goes away stops waiting, the probe finishes for the next one.
		return detection.WaitAsync(cancellationToken);
	}

	/// <summary>Turns root mode on, which asks for the sudo password when sudo needs one and none is known.</summary>
	/// <exception cref="AdminException">sudo refused, the prompt was cancelled, or root is out of reach here.</exception>
	public async Task EnableRootAsync(CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		if (LoginIsRoot)
		{
			return;
		}

		try
		{
			await _shell.EnableRootAsync(cancellationToken);
		}
		finally
		{
			Raise();
		}
	}

	/// <summary>Lets go of sudo; a change running as root finishes first.</summary>
	public void DisableRoot()
	{
		_shell.DisableRoot();
		Raise();
	}

	/// <summary>Runs a read. One marked as root needs root mode on, so reading never asks for a password by surprise.</summary>
	/// <exception cref="AdminException">The script could not run to its end, or needs root mode.</exception>
	public async Task<AdminOutcome> ReadAsync(AdminScript script, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(script);
		ThrowIfDisposed();
		bool elevated = script.AsRoot && !LoginIsRoot;
		if (elevated && !RootModeOn)
		{
			throw new AdminException(AdminTexts.RootModeNeeded);
		}

		try
		{
			return await _shell.RunAsync(script, elevated, input: null, cancellationToken);
		}
		catch (AdminException ex) when (ex.RootRefused)
		{
			Raise();
			throw;
		}
	}

	/// <summary>
	/// Runs a change the user confirmed. Root mode is turned on first when the change needs it. The change runs for the
	/// session rather than for the view, so it finishes even if the dialog closes; the result stays in <see cref="Last"/>.
	/// </summary>
	public async Task<AdminActionResult> RunAsync(AdminAction action, AdminInput? input)
	{
		ArgumentNullException.ThrowIfNull(action);
		ThrowIfDisposed();
		AdminScript script = action.Script;
		AdminActionResult started = new()
		{
			Title = action.Title,
			Commands = script.Body,
			AsRoot = script.AsRoot,
			StartedAt = _time.GetUtcNow(),
		};

		lock (_lock)
		{
			if (_running is not null)
			{
				throw new AdminException("Another change is still running on this machine. Wait for it to finish.");
			}

			_running = started;
		}

		Raise();
		AdminActionResult finished = started with { Failure = "The command stopped before it finished." };
		try
		{
			bool elevated = script.AsRoot && !LoginIsRoot;
			if (elevated && !RootModeOn)
			{
				await _shell.EnableRootAsync(_closed.Token);
			}

			AdminOutcome outcome = await _shell.RunAsync(script, elevated, input, _closed.Token);
			finished = started with { Outcome = outcome };
		}
		catch (AdminException ex)
		{
			_logger.LogInformation("System view change {Script} did not run to its end: {Failure}", script.Name, LogSafe.Describe(ex));
			finished = started with { Failure = ex.Message };
		}
		catch (OperationCanceledException) when (_closed.IsCancellationRequested)
		{
			finished = started with { Failure = "The session closed before the command finished." };
		}
		finally
		{
			lock (_lock)
			{
				_running = null;
				_last = finished;
			}

			Raise();
		}

		return finished;
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await _closed.CancelAsync();
		await _shell.DisposeAsync();
		_closed.Dispose();
	}

	private async Task<SystemProfile> DetectCoreAsync()
	{
		AdminOutcome outcome = await _shell.RunAsync(SystemProbe.Script, elevated: false, input: null, _closed.Token);
		SystemProfile profile = SystemProfile.From(SystemProbe.Parse(outcome.Output));
		Profile = profile;
		Raise();
		return profile;
	}

	private void ThrowIfDisposed()
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			throw new AdminException("The session is closed.");
		}
	}

	private void Raise() => Changed?.Invoke();
}
