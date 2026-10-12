namespace Mokaterm.Plugins.Sample.Fail2ban;

/// <summary>What <see cref="Fail2banCommands.Status"/> read: the jail names the server lists, and each jail's status in that order.</summary>
internal sealed record Fail2banStatus(IReadOnlyList<string> JailNames, IReadOnlyList<Fail2banJail> Jails)
{
	public static Fail2banStatus Empty { get; } = new([], []);

	public int BannedNow => Jails.Sum(jail => jail.CurrentlyBanned ?? jail.BannedAddresses.Count);
}
