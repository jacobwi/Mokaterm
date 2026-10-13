namespace Mokaterm.Modules.Ssh.KeepAlive;

/// <summary>How often a connection is asked whether it is alive, and how many unanswered intervals in a row end it.</summary>
/// <param name="Interval">Time between keepalives; zero or less turns them off.</param>
/// <param name="MaxMissed">Intervals in a row the server may owe a reply and send none.</param>
internal sealed record KeepAlivePolicy(TimeSpan Interval, int MaxMissed)
{
	public bool IsOn => Interval > TimeSpan.Zero && MaxMissed > 0;
}
