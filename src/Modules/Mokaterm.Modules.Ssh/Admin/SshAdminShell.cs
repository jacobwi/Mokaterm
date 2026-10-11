using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Elevation;
using Mokaterm.Modules.Ssh.Sessions;
using Mokaterm.Modules.Ssh.Shell;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>
/// The system view's scripts on an SSH session: one exec channel each on the session's own connection, and root through
/// the same sudo path the file browser uses. The sudo runner is held only while root mode is on, so a password the
/// user did not ask to remember lives no longer than the mode they can see.
/// </summary>
internal sealed class SshAdminShell : IAdminShell
{
	// The terminal holds one channel and OpenSSH allows ten; tunnels and the file browser's sudo need some of the rest.
	private const int MaxConcurrentCommands = 3;

	private readonly ISshSessionHost _host;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _slots = new(MaxConcurrentCommands, MaxConcurrentCommands);
	private readonly SemaphoreSlim _rootGate = new(1, 1);
	private readonly Lock _lock = new();
	private RootLease? _root;
	private int _disposed;

	public SshAdminShell(ISshSessionHost host, ILogger logger)
	{
		_host = host;
		_logger = logger;
	}

	public string? ElevationBlockedReason => _host.Context.Options.Elevation == SshElevationMode.None
		? "Running as root is turned off for this connection."
		: null;

	public bool HasRoot
	{
		get
		{
			lock (_lock)
			{
				return _root is not null;
			}
		}
	}

	public string Account => _host.Context.Account;

	public bool SupportsInput => true;

	public async Task EnableRootAsync(CancellationToken cancellationToken)
	{
		if (ElevationBlockedReason is { } blocked)
		{
			throw new AdminException(blocked);
		}

		await _rootGate.WaitAsync(cancellationToken);
		try
		{
			if (HasRoot)
			{
				return;
			}

			SudoRunner runner;
			try
			{
				SudoLogin login = SudoLogin.ForSession(_host, await _host.GetSshClientAsync(cancellationToken));
				runner = await SudoRunner.AuthenticateAsync(login, cancellationToken);
			}
			catch (RemoteFileSystemException ex)
			{
				throw new AdminException(ex.Message, ex);
			}

			lock (_lock)
			{
				if (Volatile.Read(ref _disposed) != 0)
				{
					runner.Dispose();
					throw new AdminException("The session is closed.");
				}

				_root = new RootLease(runner);
			}
		}
		finally
		{
			_rootGate.Release();
		}
	}

	public void DisableRoot()
	{
		RootLease? root;
		lock (_lock)
		{
			root = _root;
			_root = null;
		}

		root?.Retire();
	}

	public async Task<AdminOutcome> RunAsync(AdminScript script, bool elevated, AdminInput? input, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(script);
		if (script.ReadsInput != (input is not null))
		{
			throw new ArgumentException("A script gets input exactly when it reads stdin.", nameof(input));
		}

		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(script.Timeout);
		await _slots.WaitAsync(deadline.Token);
		try
		{
			return elevated
				? await RunAsRootAsync(script, input, deadline.Token)
				: await RunAsLoginAsync(script, input, deadline.Token);
		}
		catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
		{
			throw new AdminException(AdminTexts.TimedOut(script.Timeout), ex);
		}
		catch (RemoteCommandException ex)
		{
			throw new AdminException(ex.Message, ex);
		}
		catch (RemoteFileSystemException ex)
		{
			// The session's client getter reports a dropped connection this way.
			throw new AdminException(ex.Message, ex);
		}
		finally
		{
			_slots.Release();
		}
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return ValueTask.CompletedTask;
		}

		DisableRoot();
		return ValueTask.CompletedTask;
	}

	private async Task<AdminOutcome> RunAsLoginAsync(AdminScript script, AdminInput? input, CancellationToken cancellationToken)
	{
		SshClient client = await _host.GetSshClientAsync(cancellationToken);
		string command = PosixShell.ScriptCommand(AdminScriptText.Compose(script), []);
		using SecretBuffer? stdin = input is null ? null : AdminScriptText.FrameInput(input);
		RemoteCommandOutcome outcome = await CommandExecution.RunAsync(
			token => StartAsync(client, command, stdin, token),
			AdminScriptText.Request(script, command),
			_host.Connector.Time,
			cancellationToken);
		return AdminScriptText.ToOutcome(outcome, AdminScriptText.ErrorText(outcome));
	}

	private async Task<AdminOutcome> RunAsRootAsync(AdminScript script, AdminInput? input, CancellationToken cancellationToken)
	{
		RootLease lease = Acquire() ?? throw new AdminException("Turn on root mode first.");
		try
		{
			string body = AdminScriptText.Compose(script);
			using SecretBuffer? stdin = input is null ? null : AdminScriptText.FrameInput(input);
			RemoteCommandOutcome outcome = await lease.Runner.ExecuteAsync(
				body,
				[],
				stdin,
				AdminScriptText.Request(script, body),
				_host.Connector.Time,
				cancellationToken);

			string error = AdminScriptText.ErrorText(outcome);
			(bool ran, string sudoOutput, string scriptError) = SudoErrors.Split(error);
			if (!ran)
			{
				SudoFailure failure = SudoErrors.Classify(sudoOutput, outcome.ExitCode);
				_logger.LogInformation("sudo refused a system view command on session {SessionId} ({Failure})", _host.Context.SessionId, failure);

				// A password that stopped working, or rights taken away, must be asked for again rather than retried.
				DisableRoot();
				throw new AdminException(AdminTexts.SudoRefused(failure, _host.Context.Account, sudoOutput)) { RootRefused = true };
			}

			return AdminScriptText.ToOutcome(outcome, scriptError);
		}
		finally
		{
			lease.Release();
		}
	}

	private RootLease? Acquire()
	{
		lock (_lock)
		{
			return _root?.TryAcquire() == true ? _root : null;
		}
	}

	private static async Task<IExecChannel> StartAsync(SshClient client, string command, SecretBuffer? stdin, CancellationToken cancellationToken)
	{
		RemoteCommand started = await RemoteCommand.StartAsync(client, command, stdin, cancellationToken);
		if (stdin is null)
		{
			started.CloseInput();
		}

		return started;
	}

	/// <summary>
	/// The sudo runner while root mode is on. Turning root mode off retires it, and the runner, which holds the
	/// password, is wiped once the last command using it has finished.
	/// </summary>
	private sealed class RootLease(SudoRunner runner)
	{
		private readonly Lock _lock = new();
		private int _users;
		private bool _retired;

		public SudoRunner Runner { get; } = runner;

		public bool TryAcquire()
		{
			lock (_lock)
			{
				if (_retired)
				{
					return false;
				}

				_users++;
				return true;
			}
		}

		public void Release()
		{
			lock (_lock)
			{
				_users--;
				if (!_retired || _users > 0)
				{
					return;
				}
			}

			Runner.Dispose();
		}

		public void Retire()
		{
			lock (_lock)
			{
				if (_retired)
				{
					return;
				}

				_retired = true;
				if (_users > 0)
				{
					return;
				}
			}

			Runner.Dispose();
		}
	}
}
