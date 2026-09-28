using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Telnet.Protocol;

namespace Mokaterm.Modules.Telnet.Sessions;

/// <summary>
/// A telnet session: one socket and the terminal on it. Telnet carries nothing else, so the terminal channel is
/// the only feature; there is no file system beside it.
/// </summary>
internal sealed class TelnetSession : IProtocolSession
{
	private readonly TelnetTransport _transport;
	private readonly TelnetTerminalChannel _channel;
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Guid _sessionId;
	private readonly ILogger _logger;
	private int _disposed;

	public TelnetSession(Guid sessionId, TelnetTransport transport, TelnetTerminalChannel channel, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(transport);
		ArgumentNullException.ThrowIfNull(channel);
		_sessionId = sessionId;
		_transport = transport;
		_channel = channel;
		_logger = logger;
		_ = WatchAsync();
	}

	public Task Completion => _completion.Task;

	public TFeature? GetFeature<TFeature>()
		where TFeature : class =>
		typeof(TFeature) == typeof(ITerminalChannel) ? (TFeature)(object)_channel : null;

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_completion.TrySetResult();
		await _channel.DisposeAsync();
		await _transport.DisposeAsync();
		_logger.LogInformation("Telnet session {SessionId} closed", _sessionId);
	}

	private async Task WatchAsync()
	{
		await _channel.Ended;
		if (_channel.Failure is { } failure)
		{
			string endpoint = $"{_transport.Host}:{_transport.Port}";
			_completion.TrySetException(new TelnetProtocolException($"The connection to {endpoint} was lost: {failure.Message}", failure));
			return;
		}

		_completion.TrySetResult();
	}
}
