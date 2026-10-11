namespace Mokaterm.Abstractions.Settings;

/// <summary>
/// Running one command on several saved logins. The run dialog edits these and remembers them for the next run; the
/// command itself is never kept here, because a command line can carry a token and this file is not encrypted.
/// </summary>
public sealed record CommandRunSettings : ISettingsSection
{
	/// <summary>The <see cref="ParallelHosts"/> range: one host at a time up to what a desktop can sensibly hold open.</summary>
	public const int MinParallelHosts = 1;

	/// <inheritdoc cref="MinParallelHosts"/>
	public const int MaxParallelHosts = 32;

	/// <summary>The <see cref="TimeoutSeconds"/> range: long enough for a slow login, short enough to notice a hung host.</summary>
	public const int MinTimeoutSeconds = 5;

	/// <inheritdoc cref="MinTimeoutSeconds"/>
	public const int MaxTimeoutSeconds = 3600;

	/// <summary>
	/// The <see cref="OutputKilobytes"/> range. Every host keeps this much of each stream in the page, so forty hosts at
	/// the top of the range is already a few hundred megabytes of text.
	/// </summary>
	public const int MinOutputKilobytes = 4;

	/// <inheritdoc cref="MinOutputKilobytes"/>
	public const int MaxOutputKilobytes = 4096;

	public static string SectionKey => "commandrun";

	/// <summary>How many hosts connect and run at once.</summary>
	public int ParallelHosts { get; init; } = 8;

	/// <summary>
	/// How long one host gets, from the start of its connect to the end of its command. Time spent waiting on a prompt
	/// does not count: a user reading a host key is not a slow host.
	/// </summary>
	public int TimeoutSeconds { get; init; } = 60;

	/// <summary>How much of stdout, and again of stderr, each host keeps. The rest is counted and dropped.</summary>
	public int OutputKilobytes { get; init; } = 256;

	/// <summary>
	/// Skip a host whose connect would ask anything (a password, a passphrase, an unknown host key) instead of asking,
	/// so a run across forty machines cannot turn into forty prompts. On by default for exactly that reason.
	/// </summary>
	public bool SkipHostsThatAsk { get; init; } = true;

	public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

	public int OutputLimitBytes => OutputKilobytes * 1024;

	/// <summary>A copy with every number inside the range the run dialog offers.</summary>
	public CommandRunSettings Clamped() => this with
	{
		ParallelHosts = Math.Clamp(ParallelHosts, MinParallelHosts, MaxParallelHosts),
		TimeoutSeconds = Math.Clamp(TimeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds),
		OutputKilobytes = Math.Clamp(OutputKilobytes, MinOutputKilobytes, MaxOutputKilobytes),
	};
}
