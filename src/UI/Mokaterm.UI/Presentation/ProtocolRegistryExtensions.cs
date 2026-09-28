using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.Presentation;

internal static class ProtocolRegistryExtensions
{
	public static ProtocolDescriptor? FindDescriptor(this IProtocolRegistry registry, string protocolId) =>
		registry.Find(protocolId)?.Descriptor;

	/// <summary>The protocol's display name, or its id in uppercase when no module registered it.</summary>
	public static string DisplayName(this IProtocolRegistry registry, string protocolId) =>
		registry.Find(protocolId)?.Descriptor.DisplayName ?? protocolId.ToUpperInvariant();

	/// <summary>
	/// The file-browser-only variant of a terminal protocol, such as <c>sftp</c> for <c>ssh</c>. Null when the protocol
	/// has no terminal or no module registered such a variant.
	/// </summary>
	public static ProtocolDescriptor? FindFileBrowserVariant(this IProtocolRegistry registry, ProtocolDescriptor protocol)
	{
		if (!protocol.Has(ProtocolCapabilities.Terminal))
		{
			return null;
		}

		string family = protocol.VariantOf ?? protocol.Id;
		return registry.Protocols.FirstOrDefault(candidate =>
			string.Equals(candidate.VariantOf, family, StringComparison.OrdinalIgnoreCase)
			&& candidate.Has(ProtocolCapabilities.FileSystem)
			&& !candidate.Has(ProtocolCapabilities.Terminal));
	}

	/// <inheritdoc cref="FindFileBrowserVariant(IProtocolRegistry, ProtocolDescriptor)"/>
	public static ProtocolDescriptor? FindFileBrowserVariant(this IProtocolRegistry registry, string protocolId) =>
		registry.FindDescriptor(protocolId) is { } protocol ? registry.FindFileBrowserVariant(protocol) : null;
}
