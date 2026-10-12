using Mokaterm.Abstractions.Connections;

namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>
/// How this session logged in, as far as the view can tell: the account, the method and, for a key, its fingerprint.
/// It decides which changes would refuse this login the next time, so a change that could lock the user out says so.
/// </summary>
/// <param name="UserName">The account the session runs as on the server.</param>
/// <param name="Method">How the login proved itself, or null when the session does not say.</param>
/// <param name="KeyFingerprint">The <c>SHA256:...</c> of the key a key login used, or null when it is not known.</param>
internal sealed record SessionLogin(string UserName, AuthenticationMethod? Method, string? KeyFingerprint)
{
	/// <summary>Logs in as root itself, not through sudo.</summary>
	public bool IsRoot => UserName == "root";

	/// <summary>
	/// The login typed a password, either for sshd's password method or answering its keyboard-interactive prompts,
	/// which is how a saved password logs in where the password method is off. Unknown counts as yes.
	/// </summary>
	public bool MayUsePassword => Method is null or AuthenticationMethod.Password or AuthenticationMethod.KeyboardInteractive;

	/// <summary>The login signed with a key, saved or in an agent. Unknown counts as yes.</summary>
	public bool MayUseKey => Method is null or AuthenticationMethod.PublicKey or AuthenticationMethod.Agent;
}
