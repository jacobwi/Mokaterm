namespace Mokaterm.Plugins.Sample.Fail2ban;

/// <summary>One jail as <c>fail2ban-client status JAIL</c> prints it. A count fail2ban did not print is null.</summary>
internal sealed record Fail2banJail
{
	public required string Name { get; init; }

	public int? CurrentlyFailed { get; init; }

	public int? TotalFailed { get; init; }

	public int? CurrentlyBanned { get; init; }

	public int? TotalBanned { get; init; }

	/// <summary>The addresses banned now, as fail2ban lists them: single addresses, or networks where a jail bans those.</summary>
	public IReadOnlyList<string> BannedAddresses { get; init; } = [];

	/// <summary>What the filter reads: the log files, or the journal matches of a systemd backend.</summary>
	public string Source { get; init; } = "";

	/// <summary>What fail2ban printed instead of the jail's status, when it refused to give one.</summary>
	public string? Problem { get; init; }
}
