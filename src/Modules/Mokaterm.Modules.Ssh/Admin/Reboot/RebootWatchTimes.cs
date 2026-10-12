namespace Mokaterm.Modules.Ssh.Admin.Reboot;

/// <summary>How long reconnecting after a reboot waits for each step.</summary>
/// <param name="Down">For the session to drop after the reboot was sent.</param>
/// <param name="Back">For the server to answer again once the session dropped, reconnects included.</param>
/// <param name="FirstWait">Before the first look, since the server is still going down.</param>
/// <param name="Retry">Between looks.</param>
/// <param name="AttemptStart">For a reconnect to show as started.</param>
internal sealed record RebootWatchTimes(TimeSpan Down, TimeSpan Back, TimeSpan FirstWait, TimeSpan Retry, TimeSpan AttemptStart)
{
	/// <summary>A server takes a minute or two to boot; a slow one with a long BIOS can take five.</summary>
	public static RebootWatchTimes Default { get; } = new(
		Down: TimeSpan.FromMinutes(3),
		Back: TimeSpan.FromMinutes(10),
		FirstWait: TimeSpan.FromSeconds(10),
		Retry: TimeSpan.FromSeconds(10),
		AttemptStart: TimeSpan.FromSeconds(5));
}
