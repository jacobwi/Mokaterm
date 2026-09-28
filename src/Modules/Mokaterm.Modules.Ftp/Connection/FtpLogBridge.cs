using FluentFTP;
using Microsoft.Extensions.Logging;

namespace Mokaterm.Modules.Ftp.Connection;

/// <summary>
/// Forwards FluentFTP's protocol log to <see cref="ILogger"/> at trace level only: it includes paths. Hosts, user names
/// and passwords are masked by FluentFTP's own Log* settings, which <see cref="FtpClientOptions"/> turns off.
/// </summary>
internal sealed class FtpLogBridge : IFtpLogger
{
	private readonly ILogger _logger;

	public FtpLogBridge(ILogger logger) => _logger = logger;

	public void Log(FtpLogEntry entry)
	{
		if (_logger.IsEnabled(LogLevel.Trace))
		{
			_logger.Log(LogLevel.Trace, entry.Exception, "[{Severity}] {Message}", entry.Severity, entry.Message);
		}
	}
}
