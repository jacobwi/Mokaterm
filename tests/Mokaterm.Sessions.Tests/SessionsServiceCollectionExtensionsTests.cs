using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.Sessions.Extensions;
using Mokaterm.Sessions.Tests.Fakes;

namespace Mokaterm.Sessions.Tests;

public sealed class SessionsServiceCollectionExtensionsTests
{
	[Fact]
	public async Task AddMokatermSessions_ResolvesTheRegistryOnce_AndSessionServicesPerScope()
	{
		ServiceCollection services = new();
		services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
		services.AddSingleton<IProtocolProvider>(new FakeProtocolProvider(TestProtocols.Ssh));
		services.AddSingleton<IProtocolProvider>(new FakeProtocolProvider(TestProtocols.Sftp));
		services.AddSingleton<ISettingsService, UncachedSettingsService>();
		services.AddSingleton<IAppEnvironment>(new FakeAppEnvironment(Path.Combine(Path.GetTempPath(), "mokaterm-di-tests")));
		services.AddScoped<IConnectionRepository, FakeConnectionRepository>();
		services.AddScoped<ICredentialStore, FakeCredentialStore>();
		services.AddScoped<IHostIdentityVerifier, FakeHostIdentityVerifier>();
		services.AddScoped<IUserInteraction, FakeUserInteraction>();

		_ = services.AddMokatermSessions().AddMokatermSessions();

		_ = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISessionManager));
		await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
		Assert.Equal(2, provider.GetRequiredService<IProtocolRegistry>().Protocols.Count);

		ISessionManager first;
		await using (AsyncServiceScope scope = provider.CreateAsyncScope())
		{
			first = scope.ServiceProvider.GetRequiredService<ISessionManager>();
			Assert.Same(first, scope.ServiceProvider.GetRequiredService<ISessionManager>());
			Assert.Same(scope.ServiceProvider.GetRequiredService<ITransferQueue>(), scope.ServiceProvider.GetRequiredService<ITransferQueue>());
			Assert.Same(scope.ServiceProvider.GetRequiredService<ISessionLogRecorder>(), scope.ServiceProvider.GetRequiredService<ISessionLogRecorder>());
		}

		await using AsyncServiceScope second = provider.CreateAsyncScope();
		Assert.NotSame(first, second.ServiceProvider.GetRequiredService<ISessionManager>());
	}
}
