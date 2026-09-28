namespace Mokaterm.Abstractions.Security;

/// <summary>Thrown when encrypted data is used while the vault is locked.</summary>
public sealed class VaultLockedException : InvalidOperationException
{
	public VaultLockedException()
		: base("The vault is locked.")
	{
	}

	public VaultLockedException(string message)
		: base(message)
	{
	}

	public VaultLockedException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
