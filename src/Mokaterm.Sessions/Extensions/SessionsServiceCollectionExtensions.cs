using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.Sessions.Protocols;
using Mokaterm.Sessions.Terminal;
using Mokaterm.Sessions.Transfers;

namespace Mokaterm.Sessions.Extensions;

public static class SessionsServiceCollectionExtensions
{
	/// <summary>
	/// Registers the protocol registry (singleton), the session manager, the transfer queue and the session log recorder
	/// (all per UI scope). The session manager also needs the connection repository, credential store, host identity
	/// verifier and settings service from Core, an <c>IUserInteraction</c> from the shell, and logging; the recorder needs
	/// the host's <c>IAppEnvironment</c>.
	/// </summary>
	public static IServiceCollection AddMokatermSessions(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton<IProtocolRegistry, ProtocolRegistry>();
		services.TryAddScoped<ISessionManager, SessionManager>();
		services.TryAddScoped<ITransferQueue, TransferQueue>();
		services.TryAddScoped<ISessionLogRecorder, SessionLogRecorder>();
		return services;
	}
}
