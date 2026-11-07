using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Ssh.Connection;

/// <summary>
/// The steps of an SSH or SFTP connect as the session tab draws them, in wire order: the jump hosts, the dial up to the
/// server's greeting, the key exchange and host key check, the login, then what the session opens.
/// </summary>
internal static class SshConnectSteps
{
	public const string JumpHosts = "jump-hosts";

	public const string Connect = "connect";

	public const string HostKey = "host-key";

	public const string Login = "login";

	public const string Open = "open";

	/// <param name="opens">The last step's label, the shell or the file channel; null for a session that only runs commands.</param>
	public static IReadOnlyList<ConnectStep> For(SshConnectionOptions options, string? opens)
	{
		ArgumentNullException.ThrowIfNull(options);
		List<ConnectStep> steps = [];
		if (options.JumpConnectionIds.Count > 0)
		{
			steps.Add(new ConnectStep(JumpHosts, options.JumpConnectionIds.Count == 1 ? "Jump host" : "Jump hosts", ConnectStepKind.Route));
		}

		steps.Add(new ConnectStep(Connect, "Connect", ConnectStepKind.Dial));
		steps.Add(new ConnectStep(HostKey, "Host key", ConnectStepKind.HostIdentity));
		steps.Add(new ConnectStep(Login, "Login", ConnectStepKind.Login));
		if (opens is not null)
		{
			steps.Add(new ConnectStep(Open, opens, ConnectStepKind.Session));
		}

		return steps;
	}
}
