namespace Mokaterm.Modules.Vnc;

/// <summary>What a connection accepts when the server offers no encrypted security type.</summary>
public enum VncEncryptionMode
{
	/// <summary>Uses TLS when the server offers it and connects unencrypted otherwise. The view says which one it got.</summary>
	Preferred,

	/// <summary>Refuses the connection unless the session runs inside TLS (VeNCrypt).</summary>
	Required,
}
