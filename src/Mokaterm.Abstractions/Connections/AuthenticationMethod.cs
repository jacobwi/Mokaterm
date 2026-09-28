namespace Mokaterm.Abstractions.Connections;

public enum AuthenticationMethod
{
	/// <summary>A password, saved in the vault or asked for at connect time.</summary>
	Password,

	/// <summary>A private key saved in the vault, optionally protected by a passphrase.</summary>
	PublicKey,

	/// <summary>Server-driven prompts (one-time codes, PAM). A saved password answers "Password:" prompts.</summary>
	KeyboardInteractive,

	/// <summary>Keys held by a running SSH agent. Reserved; no module implements it yet.</summary>
	Agent,

	/// <summary>No credentials, for anonymous FTP.</summary>
	Anonymous,
}
