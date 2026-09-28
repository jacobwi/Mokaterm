using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Modules.Ssh;

/// <summary>App-wide SSH behaviour, stored under <c>ssh</c> in settings.json.</summary>
public sealed record SshSettings : ISettingsSection
{
	public const int MaxKeepAliveSeconds = 3600;

	public const int MinConnectTimeoutSeconds = 1;

	public const int MaxConnectTimeoutSeconds = 300;

	public const int MinAuthenticationAttempts = 1;

	public const int MaxAuthenticationAttempts = 10;

	public const int MinPromptTimeoutSeconds = 10;

	public const int MaxPromptTimeoutSeconds = 3600;

	public static string SectionKey => "ssh";

	/// <summary>Seconds between keepalive messages. 0 turns keepalives off.</summary>
	public int KeepAliveSeconds { get; init; } = 30;

	/// <summary>How long each connection step may wait for the server.</summary>
	public int ConnectTimeoutSeconds { get; init; } = 15;

	/// <summary>Logins tried before giving up, counting the first one.</summary>
	public int AuthenticationAttempts { get; init; } = 3;

	/// <summary>The command that runs file operations as root.</summary>
	public string SudoCommand { get; init; } = "sudo";

	/// <summary>Absolute directory where root uploads are staged before they move into place.</summary>
	public string StagingDirectory { get; init; } = "/tmp";

	/// <summary>How long a server prompt, such as a one-time code, may stay open.</summary>
	public int PromptTimeoutSeconds { get; init; } = 120;

	/// <summary>
	/// The SSH agent to talk to: a named pipe such as <c>\\.\pipe\openssh-ssh-agent</c> or a socket path. Empty looks
	/// for one, starting with <c>SSH_AUTH_SOCK</c>.
	/// </summary>
	public string AgentEndpoint { get; init; } = "";

	internal TimeSpan ConnectTimeout =>
		TimeSpan.FromSeconds(Math.Clamp(ConnectTimeoutSeconds, MinConnectTimeoutSeconds, MaxConnectTimeoutSeconds));

	internal TimeSpan PromptTimeout =>
		TimeSpan.FromSeconds(Math.Clamp(PromptTimeoutSeconds, MinPromptTimeoutSeconds, MaxPromptTimeoutSeconds));

	internal int EffectiveAuthenticationAttempts =>
		Math.Clamp(AuthenticationAttempts, MinAuthenticationAttempts, MaxAuthenticationAttempts);

	internal string EffectiveSudoCommand => IsValidSudoCommand(SudoCommand) ? SudoCommand : "sudo";

	internal string EffectiveStagingDirectory => IsValidStagingDirectory(StagingDirectory) ? StagingDirectory : "/tmp";

	/// <summary>A single command word, such as <c>sudo</c> or <c>/usr/bin/sudo</c>.</summary>
	public static bool IsValidSudoCommand(string? command) =>
		!string.IsNullOrWhiteSpace(command) && !command.Any(char.IsWhiteSpace) && command.IndexOfAny(['\'', '"', '\0']) < 0;

	public static bool IsValidStagingDirectory(string? directory) =>
		!string.IsNullOrWhiteSpace(directory) && directory.StartsWith('/') && !directory.Contains('\0', StringComparison.Ordinal);

	internal TimeSpan? GetKeepAlive(SshConnectionOptions options)
	{
		int seconds = Math.Min(options.KeepAliveSeconds ?? KeepAliveSeconds, MaxKeepAliveSeconds);
		return seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
	}
}
