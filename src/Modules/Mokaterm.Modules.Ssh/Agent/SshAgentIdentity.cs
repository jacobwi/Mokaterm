using Mokaterm.Modules.Ssh.Connection;

namespace Mokaterm.Modules.Ssh.Agent;

/// <summary>One public key an agent holds. The private half never leaves the agent.</summary>
internal sealed record SshAgentIdentity
{
	/// <summary>The public key blob, as it goes on the wire in a publickey authentication request.</summary>
	public required byte[] KeyBlob { get; init; }

	/// <summary>What the agent calls the key, usually the comment from the key file. May be empty.</summary>
	public required string Comment { get; init; }

	/// <summary>The key type inside the blob, such as <c>ssh-ed25519</c>.</summary>
	public string Algorithm => SshHostKeys.ReadKeyType(KeyBlob) ?? "unknown";

	/// <summary><c>SHA256:...</c>, the same form <c>ssh-add -l</c> prints.</summary>
	public string Fingerprint => SshHostKeys.Fingerprint(KeyBlob);

	/// <summary>The comment when there is one, otherwise the fingerprint.</summary>
	public string Display => string.IsNullOrWhiteSpace(Comment) ? Fingerprint : Comment;
}
