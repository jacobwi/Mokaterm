using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Local.Pty;

namespace Mokaterm.Modules.Local.Sessions;

/// <summary>
/// Starts a program in a pseudo console and wraps it in a session. Every protocol of this module ends here: a local
/// shell, <c>docker exec</c> and <c>kubectl exec</c> differ only in the program they name.
/// </summary>
internal sealed class LocalSessionStarter
{
	private const string DefaultTerminalType = "xterm-256color";

	private readonly ILoggerFactory _loggerFactory;
	private readonly ILogger _logger;

	public LocalSessionStarter(ILoggerFactory loggerFactory)
	{
		ArgumentNullException.ThrowIfNull(loggerFactory);
		_loggerFactory = loggerFactory;
		_logger = loggerFactory.CreateLogger<LocalSessionStarter>();
	}

	/// <summary>What a program is told the terminal is: the same name the terminal settings send over SSH.</summary>
	public static string TerminalType(ISettingsService settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		return settings.Get<TerminalSettings>().TerminalType.Trim() is { Length: > 0 } type ? type : DefaultTerminalType;
	}

	/// <param name="displayName">What the exit notice and a start failure call the program, such as <c>PowerShell</c>.</param>
	/// <param name="logKind">
	/// What the log calls it. Never the program's arguments or a container's name: a command line is where a token ends up.
	/// </param>
	public async Task<IProtocolSession> StartAsync(
		Guid sessionId,
		PseudoTerminalLaunch launch,
		string displayName,
		string logKind,
		bool reportExitCode,
		TerminalSize size,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(launch);
		ArgumentNullException.ThrowIfNull(displayName);
		LocalTerminalChannel channel = new(displayName, reportExitCode, _loggerFactory.CreateLogger<LocalTerminalChannel>());
		IPseudoTerminal terminal = await StartTerminalAsync(launch, displayName, size, channel, cancellationToken);
		channel.Attach(terminal);
		_logger.LogInformation("Local {Kind} started as process {ProcessId}", logKind, terminal.ProcessId);
		return new LocalSession(sessionId, channel, _loggerFactory.CreateLogger<LocalSession>());
	}

	private static async Task CloseWhenStartedAsync(Task<IPseudoTerminal> starting)
	{
		try
		{
			IPseudoTerminal terminal = await starting;
			await terminal.DisposeAsync();
		}
		catch (Exception)
		{
			// The start failed on its own, so there is nothing left to close.
		}
	}

	/// <summary>
	/// Starts the program off the caller's thread: creating a process can take a moment (an antivirus scanning a program
	/// on its first start), and on a Blazor host that thread is the circuit. The start takes no cancellation token, so a
	/// cancelled attempt ends whatever the start produced instead of pretending it never happened.
	/// </summary>
	private async Task<IPseudoTerminal> StartTerminalAsync(
		PseudoTerminalLaunch launch,
		string displayName,
		TerminalSize size,
		IPseudoTerminalOutput output,
		CancellationToken cancellationToken)
	{
		Task<IPseudoTerminal> starting = Task.Run(() => PseudoTerminalPlatform.Start(launch, size, output), CancellationToken.None);
		try
		{
			return await starting.WaitAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			_ = CloseWhenStartedAsync(starting);
			throw;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogInformation("A local program did not start: {Reason}", LogSafe.Describe(ex));
			throw LocalStartErrors.Describe(ex, displayName, launch);
		}
	}
}
