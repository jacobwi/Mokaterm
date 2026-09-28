using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Sessions;

/// <summary>
/// Hands a protocol another saved login, credentials included. Lives in this scope because the vault does: a module
/// is a singleton and cannot reach the store that decrypts them.
/// </summary>
internal sealed class ConnectionResolver : IConnectionResolver
{
	private readonly IConnectionRepository _repository;
	private readonly ICredentialStore _credentialStore;
	private readonly IProtocolRegistry _registry;
	private readonly IUserInteraction _interaction;
	private readonly ILogger _logger;

	public ConnectionResolver(
		IConnectionRepository repository,
		ICredentialStore credentialStore,
		IProtocolRegistry registry,
		IUserInteraction interaction,
		ILogger logger)
	{
		_repository = repository;
		_credentialStore = credentialStore;
		_registry = registry;
		_interaction = interaction;
		_logger = logger;
	}

	public async ValueTask<ResolvedConnection?> ResolveAsync(Guid connectionId, CancellationToken cancellationToken = default)
	{
		ConnectionCatalog catalog = await _repository.GetCatalogAsync(cancellationToken);
		if (catalog.FindConnection(connectionId) is not { } connection
			|| catalog.FindHost(connection.HostId) is not { } host
			|| _registry.Find(connection.ProtocolId)?.Descriptor is not { } protocol)
		{
			return null;
		}

		CredentialSource credentials = new(connection, host, protocol, isTransient: false, _credentialStore, _repository, _interaction, _logger);
		return new ResolvedConnection(host, connection, credentials);
	}
}
