using System.Security.Cryptography;

namespace Mokaterm.DevHost.Hosting;

/// <summary>The master password of this run's throwaway vault: random, held in memory only, never shown or logged.</summary>
internal sealed class DevMasterPassword
{
	public string Value { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
