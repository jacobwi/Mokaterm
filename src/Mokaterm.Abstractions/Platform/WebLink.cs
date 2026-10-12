using System.Diagnostics.CodeAnalysis;

namespace Mokaterm.Abstractions.Platform;

/// <summary>
/// What may leave the app for the user's browser: an absolute http or https address and nothing else. Any other scheme
/// is handed by the system to whatever registered it (file:, ms-settings:, an installed app's own protocol), so the
/// desktop host's link policy and every "open in the browser" ask here.
/// </summary>
public static class WebLink
{
	/// <summary>True for an absolute http or https URI.</summary>
	public static bool IsWebLink([NotNullWhen(true)] Uri? address) =>
		address is { IsAbsoluteUri: true } && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp);

	/// <summary>Reads <paramref name="address"/> as a web link, or answers false for text that is not one.</summary>
	public static bool TryCreate(string? address, [NotNullWhen(true)] out Uri? link)
	{
		if (Uri.TryCreate(address?.Trim(), UriKind.Absolute, out Uri? parsed) && IsWebLink(parsed))
		{
			link = parsed;
			return true;
		}

		link = null;
		return false;
	}
}
