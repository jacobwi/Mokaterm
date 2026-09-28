using System.Text;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Modules.Ssh.Shell;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.FileSystem;

/// <summary>
/// Owner and group names for SFTP listings, which only carry numeric ids in SFTP version 3. Read once per session through
/// an exec channel when the session has an SSH connection; without one, listings show the numbers.
/// </summary>
internal sealed class RemoteAccountNames : IDisposable
{
	// Long enough for a slow name service; past it the listing goes on with numeric ids.
	private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(15);

	private readonly Func<SshClient?> _shell;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private AccountDirectory? _directory;
	private int _disposed;

	/// <param name="shell">Returns the session's connected SSH client, or null when there is none right now.</param>
	public RemoteAccountNames(Func<SshClient?> shell, ILogger logger)
	{
		_shell = shell;
		_logger = logger;
	}

	public async ValueTask<AccountDirectory> GetAsync(CancellationToken cancellationToken)
	{
		if (_directory is not null)
		{
			return _directory;
		}

		if (Volatile.Read(ref _disposed) != 0 || _shell() is not { } client)
		{
			return AccountDirectory.Empty;
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			return _directory ??= await LoadAsync(client, cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	// The gate is not disposed: a load still holding it would throw from its finally, and a caller waiting would wait forever.
	public void Dispose() => Volatile.Write(ref _disposed, 1);

	private async Task<AccountDirectory> LoadAsync(SshClient client, CancellationToken cancellationToken)
	{
		// A name service that enumerates a huge or unreachable directory must not hold up the first listing.
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(LoadTimeout);
		try
		{
			RemoteCommandResult result = await RemoteCommand.RunAsync(client, PosixShell.ScriptCommand(RemoteScripts.Accounts, []), null, timeout.Token);
			if (!RemoteOutput.TryGetPayload(result.Output, out ReadOnlyMemory<byte> payload))
			{
				return AccountDirectory.Empty;
			}

			string text = Encoding.UTF8.GetString(payload.Span);
			int separator = text.IndexOf('\0', StringComparison.Ordinal);
			return separator < 0
				? AccountDirectory.Parse(text, "")
				: AccountDirectory.Parse(text[..separator], text[(separator + 1)..]);
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			// Names are a nicety: accounts without a shell (sftp-only) or a name service that is too slow still list, with numeric ids.
			_logger.LogDebug("Could not read account names; listings show numeric ids: {Error}", LogSafe.Describe(ex));
			return AccountDirectory.Empty;
		}
	}
}
