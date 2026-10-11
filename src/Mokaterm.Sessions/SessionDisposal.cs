using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Sessions;

/// <summary>Closes a protocol session for anyone who opened one: a tab, or a host of a command run.</summary>
internal static class SessionDisposal
{
	/// <summary>How long closing waits for a protocol session to dispose before leaving it to finish on its own.</summary>
	public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

	/// <summary>Never throws. A session that takes longer than <see cref="Timeout"/> keeps closing in the background.</summary>
	public static async Task DisposeAsync(IProtocolSession session, Guid sessionId, ILogger logger, TimeProvider timeProvider)
	{
		// Some protocol libraries close synchronously over the network, so disposal runs on the pool where the wait
		// can give up on it.
		Task disposal = Task.Run(() => session.DisposeAsync().AsTask());
		try
		{
			await disposal.WaitAsync(Timeout, timeProvider);
		}
		catch (TimeoutException)
		{
			logger.LogWarning(
				"Session {SessionId} did not close within {Seconds} seconds and keeps closing in the background.",
				sessionId,
				Timeout.TotalSeconds);
			_ = disposal.ContinueWith(
				task => logger.LogWarning(task.Exception, "Closing session {SessionId} failed.", sessionId),
				CancellationToken.None,
				TaskContinuationOptions.OnlyOnFaulted,
				TaskScheduler.Default);
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Closing session {SessionId} failed.", sessionId);
		}
	}
}
