using Microsoft.Extensions.Logging;

namespace Mokaterm.Modules.Ssh.Sessions;

/// <summary>Closes the parts of a session one by one, so a part that fails to close cannot keep the later ones open.</summary>
internal static class SessionTeardown
{
	/// <summary>Runs <paramref name="release"/>; a failure is logged and the teardown goes on.</summary>
	public static async Task ReleaseAsync(Func<ValueTask> release, ILogger logger, Guid sessionId, string part)
	{
		try
		{
			await release();
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Session {SessionId} could not close {Part} cleanly.", sessionId, part);
		}
	}
}
