using Mokaterm.Abstractions.Connections;

namespace Mokaterm.UI.Presentation;

/// <summary>
/// The colour a machine is drawn in. A host with a colour of its own keeps it; every other host gets one from a small
/// palette by its address, so the same machine always looks the same and two sessions side by side rarely match.
/// </summary>
internal static class HostAccent
{
	/// <summary>How many <c>--mt-host-N</c> colours <c>mokaterm.css</c> defines.</summary>
	private const int PaletteSize = 8;

	public static string For(HostProfile host)
	{
		ArgumentNullException.ThrowIfNull(host);
		return For(host.Address, host.Color);
	}

	/// <summary>
	/// The colour for a host that is being typed rather than saved, so the editor's preview cannot pick its own rule.
	/// A <paramref name="color"/> that is not a colour falls back the same way an empty one does.
	/// </summary>
	public static string For(string? address, string? color) =>
		HexColor.Normalize(color) ?? For(address);

	public static string For(string? address) =>
		$"var(--mt-host-{Index(address)})";

	/// <summary>
	/// FNV-1a over the lowercased address. <see cref="string.GetHashCode()"/> is randomised per process, which would
	/// give the same machine a different colour on every start.
	/// </summary>
	private static int Index(string? address)
	{
		uint hash = 2166136261;
		foreach (char c in address ?? "")
		{
			hash = (hash ^ char.ToLowerInvariant(c)) * 16777619;
		}

		return (int)(hash % PaletteSize) + 1;
	}
}
