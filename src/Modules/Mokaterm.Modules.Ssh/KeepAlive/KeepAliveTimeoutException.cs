using System.Globalization;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.KeepAlive;

/// <summary>
/// The server stopped answering keepalives, so the connection counts as lost although its socket still looks open. It is
/// an <see cref="SshConnectionException"/> so everything that treats a dropped connection as lost treats this one alike.
/// </summary>
internal sealed class KeepAliveTimeoutException : SshConnectionException
{
	public KeepAliveTimeoutException()
		: base("The server stopped answering keepalives.")
	{
	}

	public KeepAliveTimeoutException(string message)
		: base(message)
	{
	}

	public KeepAliveTimeoutException(string message, Exception inner)
		: base(message, inner)
	{
	}

	/// <summary>The exception for a connection that left <paramref name="missed"/> keepalives in a row unanswered.</summary>
	public static KeepAliveTimeoutException Missed(int missed) =>
		new(string.Create(
			CultureInfo.CurrentCulture,
			$"The server stopped answering. {missed} {(missed == 1 ? "keepalive" : "keepalives in a row")} went unanswered."));
}
