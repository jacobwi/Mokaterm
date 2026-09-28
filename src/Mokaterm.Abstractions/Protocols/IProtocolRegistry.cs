namespace Mokaterm.Abstractions.Protocols;

/// <summary>All protocol providers registered by modules.</summary>
public interface IProtocolRegistry
{
	/// <summary>Descriptors ordered by <see cref="ProtocolDescriptor.Order"/>.</summary>
	IReadOnlyList<ProtocolDescriptor> Protocols { get; }

	IProtocolProvider? Find(string protocolId);

	/// <exception cref="KeyNotFoundException">No module registered the protocol.</exception>
	IProtocolProvider Get(string protocolId);
}
