namespace Mokaterm.Abstractions.Credentials;

/// <summary>The public half of a key pair. Safe to show, copy and paste into <c>authorized_keys</c>.</summary>
/// <param name="Algorithm">Wire name such as <c>ssh-ed25519</c>.</param>
/// <param name="Comment">The label at the end of the line, empty when the key has none.</param>
/// <param name="AuthorizedKeysLine">One line: algorithm, base64 key and comment.</param>
/// <param name="Fingerprint">The <c>SHA256:...</c> fingerprint OpenSSH prints.</param>
public sealed record SshPublicKey(string Algorithm, string Comment, string AuthorizedKeysLine, string Fingerprint);
