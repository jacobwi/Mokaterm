namespace Mokaterm.Modules.Ssh.Connection;

/// <summary>
/// The logins a connect has already gone through. A jump host may name a jump host of its own, so this stops a chain
/// that points back at itself and caps how far one connect may hop.
/// </summary>
internal sealed class SshJumpTrail
{
	public const int MaxHops = SshConnectionOptions.MaxJumpHosts;

	private readonly HashSet<Guid> _visited = [];

	public int Depth { get; private set; }

	/// <summary>False when this login is already in the chain or the chain is as long as it may get.</summary>
	public bool TryEnter(Guid connectionId)
	{
		if (Depth >= MaxHops || !_visited.Add(connectionId))
		{
			return false;
		}

		Depth++;
		return true;
	}
}
