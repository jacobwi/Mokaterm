using Mokaterm.Abstractions.Interaction;
using Mokaterm.Modules.Ssh.Connection;

namespace Mokaterm.Modules.Ssh.SecurityKeys;

/// <summary>
/// What a signature that waits for the user needs from the login attempt it belongs to. SSH.NET signs on its own thread
/// in the middle of the login, while the connect watchdog counts down a step meant for the network, so the watchdog is
/// paused for the touch the way it is for a keyboard-interactive prompt, and the user is told why nothing happens.
/// </summary>
internal sealed class SecurityKeyTouch
{
	private readonly ConnectWatchdog? _watchdog;
	private readonly IUserInteraction? _interaction;
	private readonly string _account;

	/// <param name="timeout">How long the platform prompt may wait for the user: the SSH prompt timeout.</param>
	/// <param name="cancellationToken">The attempt's own cancellation, which closes the prompt.</param>
	public SecurityKeyTouch(ConnectWatchdog? watchdog, IUserInteraction? interaction, string account, TimeSpan timeout, CancellationToken cancellationToken)
	{
		_watchdog = watchdog;
		_interaction = interaction;
		_account = account;
		Timeout = timeout;
		CancellationToken = cancellationToken;
	}

	/// <summary>For a signature made outside a login attempt: nothing to pause and no one to tell.</summary>
	public static SecurityKeyTouch Unattended { get; } = new(null, null, "", TimeSpan.FromMinutes(2), CancellationToken.None);

	public TimeSpan Timeout { get; }

	public CancellationToken CancellationToken { get; }

	public T Run<T>(Func<SecurityKeyTouch, T> sign)
	{
		ArgumentNullException.ThrowIfNull(sign);
		_watchdog?.Suspend();

		// The platform shows its own prompt as well, but it can open behind the window or wait for a key to be plugged in.
		_interaction?.Notify(NoticeSeverity.Info, $"Touch your security key to log in to {_account}.", "Security key");
		try
		{
			return sign(this);
		}
		finally
		{
			_watchdog?.Arm();
		}
	}
}
