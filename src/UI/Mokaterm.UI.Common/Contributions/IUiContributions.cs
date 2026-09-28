using Moka.Red.Core.Icons;

namespace Mokaterm.UI.Common.Contributions;

/// <summary>Everything modules and the shell registered for presentation.</summary>
public interface IUiContributions
{
	IReadOnlyList<ProtocolUiDescriptor> Protocols { get; }

	/// <summary>Ordered by group, then order, then title.</summary>
	IReadOnlyList<SettingsPageDescriptor> SettingsPages { get; }

	/// <summary>Ordered by order, then title.</summary>
	IReadOnlyList<SessionToolDescriptor> SessionTools { get; }

	ProtocolUiDescriptor? FindProtocol(string protocolId);

	/// <summary>
	/// The tools registered for a protocol, ordered. Unlike <see cref="ProtocolUiDescriptor"/> nothing is inherited
	/// from a parent protocol: a tool is a live session's feature, and a variant such as <c>sftp</c> may not have it.
	/// </summary>
	IReadOnlyList<SessionToolDescriptor> FindSessionTools(string protocolId);

	/// <summary>The protocol's icon, or a generic server icon.</summary>
	MokaIconDefinition GetProtocolIcon(string protocolId);
}
