namespace Mokaterm.Core.Plugins.Gallery;

/// <summary>Where a host's gallery reads its list, and which keys the list must be signed with.</summary>
public sealed record PluginGalleryOptions
{
	/// <summary>
	/// Points a host at another copy of the list, such as a preview of the gallery built from a pull request. The copy
	/// still has to be signed with one of <see cref="TrustedKeys"/>, which no variable or setting can change.
	/// </summary>
	public const string IndexVariable = "MOKATERM_GALLERY_INDEX";

	/// <summary>The gallery's list on GitHub Pages. Its signature sits beside it as <c>index.json.sig</c>.</summary>
	public static Uri ProductionIndex { get; } = new("https://jacobwi.github.io/mokaterm-plugins/index.json");

	/// <summary>The https address of <c>index.json</c>.</summary>
	public Uri Index { get; init; } = ProductionIndex;

	/// <summary>
	/// The keys a list has to be signed with, 32 raw bytes of Ed25519 each in base64. Shipping hosts keep
	/// <see cref="PluginGalleryKeys.Production"/>; the DevHost adds the one it signs its demo gallery with, and tests add
	/// their own.
	/// </summary>
	public IReadOnlyList<string> TrustedKeys { get; init; } = PluginGalleryKeys.Production;

	/// <summary>The production gallery, or the https address <see cref="IndexVariable"/> names when it names one.</summary>
	public static PluginGalleryOptions FromEnvironment() =>
		Uri.TryCreate(Environment.GetEnvironmentVariable(IndexVariable)?.Trim(), UriKind.Absolute, out Uri? index)
		&& index.Scheme == Uri.UriSchemeHttps
			? new PluginGalleryOptions { Index = index }
			: new PluginGalleryOptions();
}
