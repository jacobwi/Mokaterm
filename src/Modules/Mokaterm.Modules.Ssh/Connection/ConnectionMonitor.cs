using Renci.SshNet;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.Connection;

/// <summary>
/// Notices when an SSH.NET client loses its connection. Socket errors raise <see cref="BaseClient.ErrorOccurred"/>, but a
/// disconnect message from the server closes the session without any public event, so the client is also polled.
/// </summary>
internal sealed class ConnectionMonitor : IAsyncDisposable
{
	private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

	private readonly BaseClient _client;
	private readonly TaskCompletionSource<Exception> _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly CancellationTokenSource _stop = new();
	private readonly Task _polling;

	public ConnectionMonitor(BaseClient client)
	{
		_client = client;
		_client.ErrorOccurred += OnErrorOccurred;
		_polling = PollAsync(_stop.Token);
	}

	/// <summary>Completes with the cause once the connection is gone. Never faults.</summary>
	public Task<Exception> Lost => _lost.Task;

	public static bool IsConnected(BaseClient client)
	{
		try
		{
			return client.IsConnected;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	public async ValueTask DisposeAsync()
	{
		_client.ErrorOccurred -= OnErrorOccurred;
		await _stop.CancelAsync();
		await _polling;
		_stop.Dispose();
	}

	private void OnErrorOccurred(object? sender, ExceptionEventArgs e) => _lost.TrySetResult(e.Exception);

	private async Task PollAsync(CancellationToken cancellationToken)
	{
		using PeriodicTimer timer = new(PollInterval);
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				if (!IsConnected(_client))
				{
					_lost.TrySetResult(new SshConnectionException("The server closed the connection."));
					return;
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Disposed.
		}
	}
}
