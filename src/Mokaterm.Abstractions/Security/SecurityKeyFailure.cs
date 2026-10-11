namespace Mokaterm.Abstractions.Security;

public enum SecurityKeyFailure
{
	/// <summary>Anything the platform did not explain.</summary>
	Failed,

	/// <summary>This machine or host has no way to reach a security key.</summary>
	Unavailable,

	/// <summary>The user closed the prompt, or the caller cancelled.</summary>
	Cancelled,

	TimedOut,

	/// <summary>No authenticator holds the credential, or none was plugged in.</summary>
	NoCredential,

	/// <summary>The authenticator cannot do what was asked, such as Ed25519 or credential protection.</summary>
	NotSupported,

	/// <summary>The platform rejected a parameter before asking the user anything.</summary>
	InvalidRequest,

	/// <summary>The authenticator already holds this credential.</summary>
	AlreadyRegistered,
}
