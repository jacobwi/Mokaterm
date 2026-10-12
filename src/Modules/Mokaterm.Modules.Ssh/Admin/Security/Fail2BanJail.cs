namespace Mokaterm.Modules.Ssh.Admin.Security;

/// <summary>One fail2ban jail as <c>fail2ban-client status JAIL</c> reports it.</summary>
internal sealed record Fail2BanJail
{
	public required string Name { get; init; }

	public int? CurrentlyFailed { get; init; }

	public int? TotalFailed { get; init; }

	public int? CurrentlyBanned { get; init; }

	public int? TotalBanned { get; init; }

	/// <summary>The addresses banned now.</summary>
	public IReadOnlyList<string> Banned { get; init; } = [];

	/// <summary>What the jail watches: its log files or its journal match.</summary>
	public string Watches { get; init; } = "";
}
