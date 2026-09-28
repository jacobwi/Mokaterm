using System.Collections.Frozen;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Sessions.Protocols;

/// <summary>Every <see cref="IProtocolProvider"/> registered by modules, looked up by id without regard to case.</summary>
internal sealed class ProtocolRegistry : IProtocolRegistry
{
	private readonly FrozenDictionary<string, IProtocolProvider> _providers;

	/// <exception cref="InvalidOperationException">A provider has no id, or two providers share one.</exception>
	public ProtocolRegistry(IEnumerable<IProtocolProvider> providers)
	{
		Dictionary<string, IProtocolProvider> byId = new(StringComparer.OrdinalIgnoreCase);
		foreach (IProtocolProvider provider in providers)
		{
			ProtocolDescriptor descriptor = provider.Descriptor;
			if (string.IsNullOrWhiteSpace(descriptor.Id))
			{
				throw new InvalidOperationException($"The protocol provider {provider.GetType().FullName} has no protocol id.");
			}

			if (!byId.TryAdd(descriptor.Id, provider))
			{
				throw new InvalidOperationException(
					$"The protocol id '{descriptor.Id}' is registered twice, by {byId[descriptor.Id].GetType().FullName} and {provider.GetType().FullName}. Each protocol id can be registered once.");
			}
		}

		_providers = byId.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
		Protocols =
		[
			.. byId.Values
				.Select(provider => provider.Descriptor)
				.OrderBy(descriptor => descriptor.Order)
				.ThenBy(descriptor => descriptor.DisplayName, StringComparer.CurrentCultureIgnoreCase),
		];
	}

	public IReadOnlyList<ProtocolDescriptor> Protocols { get; }

	public IProtocolProvider? Find(string protocolId) =>
		_providers.TryGetValue(protocolId, out IProtocolProvider? provider) ? provider : null;

	public IProtocolProvider Get(string protocolId) =>
		Find(protocolId) ?? throw new KeyNotFoundException($"No module registered the protocol '{protocolId}'.");
}
