using Mokaterm.Modules.Ssh.Agent;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Ssh.Components;

/// <summary>
/// The SSH page in the Protocols group of Settings. The fields offer the ranges and refuse anything outside them;
/// what a hand-edited settings.json holds is corrected where it is used, by <see cref="SshSettings"/> itself.
/// </summary>
public partial class SshSettingsPage : SettingsSectionBase<SshSettings>
{
	private const string SudoCommandError = "Enter one command, such as sudo or /usr/bin/sudo.";

	private const string StagingDirectoryError = "Enter an absolute directory, such as /tmp.";

	private const string AgentEndpointError = "Enter a named pipe or a socket path, or leave it empty.";

	private static string AgentPlaceholder =>
		OperatingSystem.IsWindows() ? SshAgentEndpoints.OpenSshPipeName : "/run/user/1000/ssh-agent.socket";

	private static string AgentHelp =>
		"A named pipe or a socket path. Empty looks for an agent, starting with SSH_AUTH_SOCK, then OpenSSH, then Pageant.";

	private static string? ValidateSudoCommand(string value) =>
		SshSettings.IsValidSudoCommand(value) ? null : SudoCommandError;

	private static string? ValidateStagingDirectory(string value) =>
		SshSettings.IsValidStagingDirectory(value) ? null : StagingDirectoryError;

	private static string? ValidateAgentEndpoint(string value) =>
		SshAgentEndpoints.Parse(value) is not null ? null : AgentEndpointError;
}
