using Microsoft.Extensions.Logging;

namespace Mokaterm.Sessions;

internal static class EventRaiser
{
	/// <summary>
	/// Invokes every subscriber. A subscriber that throws is logged and skipped, so a broken view cannot stop the
	/// connect flow, the terminal pump or the transfer queue that raised the event.
	/// </summary>
	public static void Raise(Action? handler, ILogger logger, string eventName)
	{
		if (handler is null)
		{
			return;
		}

		foreach (Delegate subscriber in handler.GetInvocationList())
		{
			try
			{
				((Action)subscriber)();
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "A {EventName} handler threw.", eventName);
			}
		}
	}
}
