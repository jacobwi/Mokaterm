using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Fleet;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.Sessions.Commands;
using Mokaterm.Sessions.Protocols;
using Mokaterm.Sessions.Terminal;
using Mokaterm.Sessions.Transfers;

namespace Mokaterm.Sessions.Extensions;

public static class SessionsServiceCollectionExtensions
{
	/// <summary>
	/// Registers the protocol registry (singleton), the session manager, the transfer queue, the session log recorder and
	/// the runner behind command runs and surveys (all per UI scope). The session manager also needs the connection
	/// repository, credential store, host identity verifier and settings service from Core, an <c>IUserInteraction</c>
	/// from the shell, and logging; the recorder needs the host's <c>IAppEnvironment</c>, and the runner the known hosts
	/// store.
	/// </summary>
	public static IServiceCollection AddMokatermSessions(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton<IProtocolRegistry, ProtocolRegistry>();
		services.TryAddScoped<ISessionManager, SessionManager>();
		services.TryAddScoped<ITransferQueue, TransferQueue>();
		services.TryAddScoped<ISessionLogRecorder, SessionLogRecorder>();

		// One per process: every window's recorder writes into the same folder, and only this one sees all of their files.
		services.TryAddSingleton<SessionLogRetention>();

		services.TryAddScoped<HostRunner>();

		// One runner per scope serves both, so a survey and a command run share its list of runs to end with the scope.
		services.TryAddScoped<ICommandRunner>(static provider => provider.GetRequiredService<HostRunner>());
		services.TryAddScoped<IHostSurveyRunner>(static provider => provider.GetRequiredService<HostRunner>());
		return services;
	}

	/// <summary>
	/// Deletes old session logs in the background, by the rules in <see cref="Abstractions.Settings.SessionLogSettings"/>,
	/// which keep every file until the user sets one. Hosts call it once they are up, so the start never waits on the disk.
	/// </summary>
	public static void StartSessionLogCleanup(this IServiceProvider services)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.GetService<SessionLogRetention>()?.ScheduleCleanup();
	}
}
