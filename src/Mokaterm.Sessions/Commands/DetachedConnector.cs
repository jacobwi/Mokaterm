using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Sessions.Commands;

/// <summary>
/// Opens a saved login without a tab. The provider gets what a tab's connect gives it (a credential source over this
/// scope's vault, a resolver for jump hosts, the login's own proxy options), so every route a tab can take works here
/// too; only the prompts, the verifier and the purpose are the caller's. Nothing joins the session list, and the
/// caller owns the session it gets back.
/// </summary>
internal sealed class DetachedConnector
{
	private readonly IConnectionRepository _repository;
	private readonly ICredentialStore _credentialStore;
	private readonly IProtocolRegistry _registry;
	private readonly ILogger _logger;

	public DetachedConnector(IConnectionRepository repository, ICredentialStore credentialStore, IProtocolRegistry registry, ILogger logger)
	{
		_repository = repository;
		_credentialStore = credentialStore;
		_registry = registry;
		_logger = logger;
	}

	/// <exception cref="ProtocolConnectException">Connecting or logging in failed.</exception>
	/// <exception cref="VaultLockedException">The vault locked while the credentials were read.</exception>
	public async Task<IProtocolSession> ConnectAsync(DetachedConnect request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		using CredentialSource credentials = new(
			request.Connection,
			request.Host,
			request.Protocol,
			isTransient: false,
			_credentialStore,
			_repository,
			request.Interaction,
			_logger);

		ProtocolConnectContext context = new()
		{
			SessionId = request.SessionId,
			Host = request.Host,
			Connection = request.Connection,
			Port = request.Connection.Port ?? request.Protocol.DefaultPort,
			Credentials = credentials,
			HostVerifier = request.Verifier,
			Interaction = request.Interaction,
			Status = request.Status,
			Purpose = request.Purpose,

			// Jump hosts ask through the same prompts, so a run that skips hosts that ask also skips a hop that would.
			Resolver = new ConnectionResolver(_repository, _credentialStore, _registry, request.Interaction, _logger),
		};

		IProtocolSession session = await _registry.Get(request.Protocol.Id).ConnectAsync(context, cancellationToken);

		// The server took the login, so a password the user asked to keep is kept, as it would be for a tab.
		await credentials.SavePendingAsync(cancellationToken);
		return session;
	}
}
