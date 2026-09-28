using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Serial.Protocol;

namespace Mokaterm.Modules.Serial.Sessions;

/// <summary>
/// A serial session: one open port and the terminal on it. A serial line carries nothing else, so the terminal
/// channel and the panel that shows the line are the only features; there is no file system beside them.
/// </summary>
internal sealed class SerialSession : IProtocolSession
{
	private readonly SerialTerminalChannel _channel;
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly string _portName;
	private readonly Guid _sessionId;
	private readonly ILogger _logger;
	private int _disposed;

	public SerialSession(Guid sessionId, string portName, SerialTerminalChannel channel, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(channel);
		_sessionId = sessionId;
		_portName = portName;
		_channel = channel;
		_logger = logger;
		_ = WatchAsync();
	}

	public Task Completion => _completion.Task;

	public TFeature? GetFeature<TFeature>()
		where TFeature : class =>
		typeof(TFeature) == typeof(ITerminalChannel) || typeof(TFeature) == typeof(ISerialPortFeature)
			? _channel as TFeature
			: null;

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_completion.TrySetResult();
		await _channel.DisposeAsync();
		_logger.LogInformation("Serial session {SessionId} on {Port} closed", _sessionId, _portName);
	}

	private async Task WatchAsync()
	{
		await _channel.Ended;
		if (_channel.Failure is { } failure)
		{
			_completion.TrySetException(failure is SerialProtocolException
				? failure
				: new SerialProtocolException($"The connection to {_portName} was lost: {failure.Message}", failure));
			return;
		}

		_completion.TrySetResult();
	}
}
