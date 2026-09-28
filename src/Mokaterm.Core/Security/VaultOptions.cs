namespace Mokaterm.Core.Security;

/// <summary>Argon2id parameters for new vaults and password changes. Registered in DI so tests can use tiny values.</summary>
internal sealed record VaultOptions
{
	public int Argon2MemoryKiB { get; init; } = 65_536;

	public int Argon2Iterations { get; init; } = 3;

	public int Argon2Parallelism { get; init; } = 4;

	/// <summary>How often the idle auto-lock compares the last activity with the setting.</summary>
	public TimeSpan IdleCheckInterval { get; init; } = TimeSpan.FromSeconds(15);
}
