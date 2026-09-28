using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>A registry with nothing in it, for views and actions that only ask it about protocols they never reach.</summary>
internal sealed class NoProtocols : IProtocolRegistry
{
	public IReadOnlyList<ProtocolDescriptor> Protocols => [];

	public IProtocolProvider? Find(string protocolId) => null;

	public IProtocolProvider Get(string protocolId) => throw new KeyNotFoundException(protocolId);
}
