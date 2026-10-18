namespace Mokaterm.Core.Keys;

/// <summary>What a PuTTY key file says about itself in the clear: its type, comment, public key and whether it is protected.</summary>
internal sealed record PuttyPublicPart(string Algorithm, string Comment, byte[] PublicBlob, bool IsEncrypted);
