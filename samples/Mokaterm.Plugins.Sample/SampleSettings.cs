using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Plugins.Sample;

/// <summary>
/// The sample's own settings, stored under <c>plugin.moka.sample</c> in settings.json. A plugin's keys start with
/// <c>plugin.</c> and its id, which the loader checks, so they can never land on one of Mokaterm's own sections.
/// </summary>
public sealed record SampleSettings : ISettingsSection
{
	public const int MinFail2banRefreshSeconds = 5;

	public const int MaxFail2banRefreshSeconds = 3600;

	public const int MaxSubnetStartLength = 64;

	public const string DefaultSubnetStart = "192.168.1.10/24";

	public static string SectionKey => "plugin.moka.sample";

	/// <summary>How often the Fail2ban page reads the jails again while it is shown.</summary>
	public int Fail2banRefreshSeconds { get; init; } = 30;

	/// <summary>What the subnet calculator shows when it opens.</summary>
	public string SubnetStart { get; init; } = DefaultSubnetStart;

	/// <summary>A copy with every value inside the range the settings page allows.</summary>
	public SampleSettings Clamped() => this with
	{
		Fail2banRefreshSeconds = Math.Clamp(Fail2banRefreshSeconds, MinFail2banRefreshSeconds, MaxFail2banRefreshSeconds),
		SubnetStart = string.IsNullOrWhiteSpace(SubnetStart) || SubnetStart.Trim().Length > MaxSubnetStartLength
			? DefaultSubnetStart
			: SubnetStart.Trim(),
	};
}
