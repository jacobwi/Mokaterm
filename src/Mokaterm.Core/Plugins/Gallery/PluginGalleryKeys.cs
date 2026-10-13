namespace Mokaterm.Core.Plugins.Gallery;

/// <summary>
/// The Ed25519 public keys whose signature on the gallery's list this build accepts, 32 raw bytes each in base64. A new
/// key goes in here a release before the gallery signs with it, and the old one comes out a release after, so no version
/// of Mokaterm loses the gallery when the key changes.
/// </summary>
public static class PluginGalleryKeys
{
	/// <summary>
	/// gallery-2026: the public half of the gallery repository's <c>GALLERY_SIGNING_KEY</c> secret, published there as
	/// <c>signing/gallery-2026.pub.pem</c>.
	/// </summary>
	public const string Gallery2026 = "QJ26EO3DeujvVNqCciJKtByZJqouNos8pdQjpMqQZmE=";

	/// <summary>Every key a shipping build trusts.</summary>
	public static IReadOnlyList<string> Production { get; } = [Gallery2026];
}
