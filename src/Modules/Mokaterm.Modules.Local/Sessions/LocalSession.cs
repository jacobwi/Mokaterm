using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Modules.Local.Sessions;

/// <summary>
/// A local shell session: one program in one pseudo console, and the terminal on it. The files are this computer's
/// own, so there is no file system beside it.
/// </summary>
/// <remarks>
/// A shell that exits ends the session cleanly, whatever its code, so the shell offers Enter to start it again and
/// never reconnects by itself: a program that fails at once would otherwise be started over and over.
/// </remarks>
internal sealed class LocalSession : IProtocolSession
{
	private readonly LocalTerminalChannel _channel;
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Guid _sessionId;
	private readonly ILogger _logger;
	private int _disposed;

	public LocalSession(Guid sessionId, LocalTerminalChannel channel, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(channel);
		_sessionId = sessionId;
		_channel = channel;
		_logger = logger;
		_ = WatchAsync();
	}

	public Task Completion => _completion.Task;

	public TFeature? GetFeature<TFeature>()
		where TFeature : class =>
		typeof(TFeature) == typeof(ITerminalChannel) ? _channel as TFeature : null;

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_completion.TrySetResult();
		await _channel.DisposeAsync();
		_logger.LogInformation("Local shell session {SessionId} closed", _sessionId);
	}

	private async Task WatchAsync()
	{
		await _channel.Ended;
		if (_channel.Failure is { } failure)
		{
			_completion.TrySetException(failure);
			return;
		}

		_completion.TrySetResult();
	}
}
